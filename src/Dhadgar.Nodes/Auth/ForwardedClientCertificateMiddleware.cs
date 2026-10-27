using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dhadgar.Contracts.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Dhadgar.Nodes.Auth;

/// <summary>
/// Accepts a TLS client certificate forwarded by the Gateway in the
/// <see cref="AgentCertificateProfile.ForwardedCertificateHeader"/> header (D-S25, C6).
/// </summary>
/// <remarks>
/// The header is always removed from the request. It is used as the connection's client certificate
/// only when all of these hold: forwarding is enabled, the remote address is inside a trusted proxy CIDR,
/// there is exactly one header value of at most <see cref="ForwardedCertificateOptions.MaxHeaderLength"/>
/// characters, the connection has no certificate of its own, and the value parses as base64 DER.
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

        var accepted = false;
        if (_options.Enabled && headerValues.Count > 0)
        {
            accepted = TryAcceptCertificate(context, headerValues);
        }

        try
        {
            await _next(context);
        }
        finally
        {
            // The connection can outlive this request (keep-alive, HTTP/2 streams), so a forwarded
            // certificate must never stay on it for the next request, which may come from another agent.
            if (accepted)
            {
                context.Connection.ClientCertificate = null;
            }
        }
    }

    /// <summary>
    /// Sets the forwarded certificate on the connection when every acceptance rule holds.
    /// </summary>
    /// <returns>True when this request set <c>Connection.ClientCertificate</c>, so the caller must clear it.</returns>
    private bool TryAcceptCertificate(HttpContext context, StringValues headerValues)
    {
        if (!IsTrustedProxy(context.Connection.RemoteIpAddress))
        {
            _logger.LogWarning(
                "Ignored forwarded client certificate from untrusted address {RemoteIp}",
                context.Connection.RemoteIpAddress);
            return false;
        }

        if (headerValues.Count != 1)
        {
            _logger.LogWarning("Ignored forwarded client certificate: expected one header value, got {Count}",
                headerValues.Count);
            return false;
        }

        var value = headerValues[0];
        if (string.IsNullOrEmpty(value) || value.Length > _options.MaxHeaderLength)
        {
            _logger.LogWarning("Ignored forwarded client certificate: header length {Length} is not accepted",
                value?.Length ?? 0);
            return false;
        }

        // A certificate from the TLS handshake always wins over a forwarded one.
        if (context.Connection.ClientCertificate is not null)
        {
            return false;
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
            return false;
        }
        catch (CryptographicException)
        {
            _logger.LogWarning("Ignored forwarded client certificate: value is not a DER certificate");
            return false;
        }

        context.Response.RegisterForDispose(certificate);
        context.Connection.ClientCertificate = certificate;

        _logger.LogDebug("Accepted forwarded client certificate from trusted proxy {RemoteIp}",
            context.Connection.RemoteIpAddress);
        return true;
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
}
