namespace Dhadgar.Gateway.Options;

/// <summary>
/// Configuration options for Cloudflare proxy IP ranges.
/// IP ranges are fetched dynamically from Cloudflare's published endpoints.
/// Fallback ranges can be configured for offline/air-gapped scenarios.
/// </summary>
public class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>
    /// Whether to enable dynamic IP fetching from Cloudflare.
    /// Default: true. Set to false to only use fallback ranges.
    /// </summary>
    public bool EnableDynamicFetch { get; set; } = true;

    /// <summary>
    /// How often to refresh IP ranges from Cloudflare (in minutes).
    /// Default: 60 minutes. Cloudflare IPs rarely change.
    /// </summary>
    public int RefreshIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Timeout for fetching IP ranges from Cloudflare (in seconds).
    /// Default: 30 seconds.
    /// </summary>
    public int FetchTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Fallback IPv4 ranges for offline/air-gapped scenarios.
    /// Used when dynamic fetch is disabled or fails on startup.
    /// See https://www.cloudflare.com/ips-v4
    /// </summary>
    public string[] FallbackIPv4Ranges { get; set; } = [];

    /// <summary>
    /// Fallback IPv6 ranges for offline/air-gapped scenarios.
    /// Used when dynamic fetch is disabled or fails on startup.
    /// See https://www.cloudflare.com/ips-v6
    /// </summary>
    public string[] FallbackIPv6Ranges { get; set; } = [];
}
