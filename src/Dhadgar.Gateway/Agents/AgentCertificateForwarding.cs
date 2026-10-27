using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Dhadgar.Gateway.Agents;

/// <summary>
/// Gateway side of agent client-certificate ingress (D-S25). Recognises agent-issued certificates
/// and forwards the presented certificate to Nodes in <see cref="AgentCertificateProfile.ForwardedCertificateHeader"/>.
/// </summary>
public static class AgentCertificateForwarding
{
    /// <summary>Route metadata key. The value "true" opts a route into forwarding the client certificate.</summary>
    public const string MetadataKey = "ForwardClientCertificate";

    /// <summary>True when the certificate's issuer CN is the agent CA's CN. Nodes does the full validation.</summary>
    public static bool IsAgentIssued(X509Certificate2 certificate) =>
        string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
            AgentCertificateProfile.CaCommonName, StringComparison.Ordinal);

    /// <summary>
    /// Strips the forwarded-certificate header on every request, so a caller can never spoof it.
    /// Re-adds it from the connection's certificate only on routes that opt in with <see cref="MetadataKey"/>.
    /// </summary>
    public static void Apply(TransformBuilderContext context)
    {
        context.AddRequestHeaderRemove(AgentCertificateProfile.ForwardedCertificateHeader);
        if (context.Route.Metadata?.TryGetValue(MetadataKey, out var value) == true
            && bool.TryParse(value, out var enabled) && enabled)
        {
            context.AddRequestTransform(transform =>
            {
                var cert = transform.HttpContext.Connection.ClientCertificate;
                if (cert is not null)
                {
                    transform.ProxyRequest.Headers.TryAddWithoutValidation(
                        AgentCertificateProfile.ForwardedCertificateHeader,
                        Convert.ToBase64String(cert.RawData));
                }
                return ValueTask.CompletedTask;
            });
        }
    }
}
