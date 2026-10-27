using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Dhadgar.Agent.Windows.Uat.Stack;

/// <summary>
/// Builds the HTTP clients the smoke steps use against Nodes and the Gateway (P19). Each call returns a new handler:
/// TLS sessions are cached per handler, so reusing one would hide a certificate change. Server certificate
/// validation is always the default; no callback is ever set.
/// </summary>
internal static class GatewayClients
{
    /// <summary>The per-request timeout for smoke probes.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A client with redirects off. When <paramref name="clientCertificate"/> is given it is offered for the TLS
    /// client handshake. <paramref name="timeout"/> defaults to <see cref="ProbeTimeout"/>; the enroll and negotiate
    /// requests pass a longer one, because the first request through a fresh stack pays JIT and migration cost (N3).
    /// The caller disposes the client.
    /// </summary>
    public static HttpClient Create(X509Certificate2? clientCertificate = null, TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };

        try
        {
            if (clientCertificate is not null)
            {
                handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
                handler.SslOptions.LocalCertificateSelectionCallback =
                    (_, _, _, _, _) => clientCertificate;
            }

            return new HttpClient(handler, disposeHandler: true) { Timeout = timeout ?? ProbeTimeout };
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }
}
