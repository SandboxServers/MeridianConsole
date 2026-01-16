namespace Dhadgar.Gateway.Options;

/// <summary>
/// Configuration options for Cloudflare proxy IP ranges.
/// These IPs are used to validate X-Forwarded-For headers.
/// </summary>
public class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>
    /// Cloudflare IPv4 ranges. See https://www.cloudflare.com/ips-v4
    /// </summary>
    public string[] IPv4Ranges { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cloudflare IPv6 ranges. See https://www.cloudflare.com/ips-v6
    /// </summary>
    public string[] IPv6Ranges { get; set; } = Array.Empty<string>();
}
