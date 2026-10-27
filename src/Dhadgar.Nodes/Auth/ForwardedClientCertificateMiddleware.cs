using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Accepts a TLS client certificate forwarded by the Gateway in the
/// <see cref="AgentCertificateProfile.ForwardedCertificateHeader"/> header (D-S25, C6).
/// </summary>
/// <remarks>
/// The header is always removed from the request. It is used as the client certificate of that request
/// only when all of these hold: forwarding is enabled, the remote address is inside a trusted proxy CIDR,
/// there is exactly one header value of at most <see cref="ForwardedCertificateOptions.MaxHeaderLength"/>
/// characters, the connection has no certificate of its own, and the value parses as base64 DER.
/// The connection itself is never written, because HTTP/2 streams share it.
/// Chain, SPIFFE and revocation checks stay in <see cref="MtlsMiddleware"/> and
/// <see cref="ICertificateValidationService"/>. Production trust model: D-S210.
/// </remarks>
public sealed class ForwardedClientCertificateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ForwardedCertificateOptions _options;
    private readonly ILogger<ForwardedClientCertificateMiddleware> _logger;
    private readonly IReadOnlyList<IPNetwork> _trustedProxies;

    public ForwardedClientCertificateMiddleware(
        RequestDelegate next,
        IOptions<MtlsOptions> options,
        ILogger<ForwardedClientCertificateMiddleware> logger)
    {
        _next = next;
        _options = options.Value.ForwardedCertificate;
        _logger = logger;
        _trustedProxies = ParseTrustedProxies(_options.TrustedProxies);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Always strip the header so a downstream component can never read a value sent by a client.
        var headerValues = context.Request.Headers[AgentCertificateProfile.ForwardedCertificateHeader];
        context.Request.Headers.Remove(AgentCertificateProfile.ForwardedCertificateHeader);

        if (_options.Enabled && headerValues.Count > 0)
        {
            TryAcceptCertificate(context, headerValues);
        }

        await _next(context);
    }

    /// <summary>
    /// Attaches the forwarded certificate to this request when every acceptance rule holds.
    /// </summary>
    /// <remarks>
    /// The certificate is set on the request's feature collection, never on <c>Connection.ClientCertificate</c>.
    /// HTTP/2 streams share one connection, so a connection-level write would let a concurrent stream from another
    /// agent read this certificate. The request feature dies with the request.
    /// </remarks>
    private void TryAcceptCertificate(HttpContext context, StringValues headerValues)
    {
        if (!IsTrustedProxy(context.Connection.RemoteIpAddress))
        {
            _logger.LogWarning(
                "Ignored forwarded client certificate from untrusted address {RemoteIp}",
                context.Connection.RemoteIpAddress);
            return;
        }

        if (headerValues.Count != 1)
        {
            _logger.LogWarning("Ignored forwarded client certificate: expected one header value, got {Count}",
                headerValues.Count);
            return;
        }

        var value = headerValues[0];
        if (string.IsNullOrEmpty(value) || value.Length > _options.MaxHeaderLength)
        {
            _logger.LogWarning("Ignored forwarded client certificate: header length {Length} is not accepted",
                value?.Length ?? 0);
            return;
        }

        // A certificate from the TLS handshake always wins over a forwarded one. The feature is read from the
        // request features, so no ConnectionInfo cache is filled that would hide the feature set below.
        if (context.Features.Get<ITlsConnectionFeature>()?.ClientCertificate is not null)
        {
            return;
        }

        X509Certificate2? certificate = null;
        try
        {
            var der = Convert.FromBase64String(value);
            certificate = X509CertificateLoader.LoadCertificate(der);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Ignored forwarded client certificate: value is not base64");
            return;
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Ignored forwarded client certificate: value is not a DER certificate");
            return;
        }

        context.Response.RegisterForDispose(certificate);
        context.Features.Set<ITlsConnectionFeature>(new ForwardedTlsConnectionFeature(certificate));

        _logger.LogDebug("Accepted forwarded client certificate from trusted proxy {RemoteIp}",
            context.Connection.RemoteIpAddress);
    }

    private bool IsTrustedProxy(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
        {
            return false;
        }

        // Dual-stack listeners report IPv4 peers as IPv4-mapped IPv6 addresses.
        var normalized = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;

        foreach (var network in _trustedProxies)
        {
            if (network.Contains(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<IPNetwork> ParseTrustedProxies(IEnumerable<string> entries)
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in entries)
        {
            // A /0 prefix would trust every peer, so it is skipped even when startup validation was bypassed.
            if (IPNetwork.TryParse(entry, out var network) && network.PrefixLength > 0)
            {
                networks.Add(network);
            }
        }

        return networks;
    }

    /// <summary>
    /// Presents the forwarded certificate as the client certificate of one request only.
    /// </summary>
    private sealed class ForwardedTlsConnectionFeature(X509Certificate2 certificate) : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; } = certificate;

        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClientCertificate);
    }
}
