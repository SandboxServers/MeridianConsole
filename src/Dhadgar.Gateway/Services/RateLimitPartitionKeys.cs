using System.Net;
using System.Net.Sockets;
using System.Security.Claims;

namespace Dhadgar.Gateway.Services;

/// <summary>Partition keys shared by the Auth, PerTenant and PerAgent rate-limit policies.</summary>
public static class RateLimitPartitionKeys
{
    /// <summary>
    /// PerTenant key: the validated principal's org_id when it has one, else the client IP (IPv6 /64).
    /// The prefixes keep an org ID from ever colliding with an IP bucket.
    /// </summary>
    public static string Tenant(ClaimsPrincipal? user, IPAddress? ip)
    {
        var orgId = user?.Identity?.IsAuthenticated == true ? user.FindFirst("org_id")?.Value : null;
        return string.IsNullOrWhiteSpace(orgId) ? $"tenant-ip:{ClientIp(ip)}" : $"tenant-org:{orgId}";
    }

    /// <summary>
    /// IPv4 (and IPv4-mapped IPv6) keeps the full address. Other IPv6 is reduced to its /64 prefix so that
    /// rotating addresses inside one allocation does not escape the limit. Link-local and null get fixed buckets.
    /// </summary>
    public static string ClientIp(IPAddress? ip)
    {
        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv6LinkLocal)
        {
            return "unknown-linklocal";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !ip.IsIPv4MappedToIPv6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString();
        }

        return ip.ToString();
    }
}
