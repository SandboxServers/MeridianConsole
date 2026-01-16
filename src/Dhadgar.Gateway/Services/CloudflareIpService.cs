using System.Net;
using Microsoft.Extensions.Options;
using Dhadgar.Gateway.Options;

namespace Dhadgar.Gateway.Services;

/// <summary>
/// Service that fetches Cloudflare IP ranges from their published endpoints.
/// See: https://www.cloudflare.com/ips/
/// </summary>
public interface ICloudflareIpService
{
    /// <summary>
    /// Gets the current set of known Cloudflare IP networks.
    /// </summary>
    IReadOnlyList<IPNetwork> GetKnownNetworks();

    /// <summary>
    /// Refreshes the IP ranges from Cloudflare's published endpoints.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public class CloudflareIpService : ICloudflareIpService
{
    private const string IPv4Url = "https://www.cloudflare.com/ips-v4";
    private const string IPv6Url = "https://www.cloudflare.com/ips-v6";

    private readonly HttpClient _httpClient;
    private readonly ILogger<CloudflareIpService> _logger;
    private readonly CloudflareOptions _options;

    private List<IPNetwork> _knownNetworks = new();
    private readonly object _lock = new();

    public CloudflareIpService(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareIpService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Initialize with fallback IPs if configured
        InitializeWithFallback();
    }

    public IReadOnlyList<IPNetwork> GetKnownNetworks()
    {
        lock (_lock)
        {
            return _knownNetworks.AsReadOnly();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching Cloudflare IP ranges from {IPv4Url} and {IPv6Url}", IPv4Url, IPv6Url);

            var ipv4Task = FetchIpRangesAsync(IPv4Url, cancellationToken);
            var ipv6Task = FetchIpRangesAsync(IPv6Url, cancellationToken);

            await Task.WhenAll(ipv4Task, ipv6Task);

            var ipv4Ranges = await ipv4Task;
            var ipv6Ranges = await ipv6Task;

            var networks = new List<IPNetwork>();

            foreach (var range in ipv4Ranges.Concat(ipv6Ranges))
            {
                if (IPNetwork.TryParse(range, out var network))
                {
                    networks.Add(network);
                }
                else
                {
                    _logger.LogWarning("Failed to parse Cloudflare IP range: {Range}", range);
                }
            }

            if (networks.Count == 0)
            {
                _logger.LogWarning("No valid IP ranges fetched from Cloudflare. Keeping existing ranges.");
                return;
            }

            lock (_lock)
            {
                _knownNetworks = networks;
            }

            _logger.LogInformation(
                "Successfully refreshed Cloudflare IP ranges: {IPv4Count} IPv4, {IPv6Count} IPv6, {TotalCount} total",
                ipv4Ranges.Count,
                ipv6Ranges.Count,
                networks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Cloudflare IP ranges. Keeping existing ranges.");
        }
    }

    private async Task<List<string>> FetchIpRangesAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetStringAsync(url, cancellationToken);
        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private void InitializeWithFallback()
    {
        // Use fallback IPs from configuration if available (for offline/air-gapped scenarios)
        var fallbackNetworks = new List<IPNetwork>();

        foreach (var range in _options.FallbackIPv4Ranges ?? [])
        {
            if (IPNetwork.TryParse(range, out var network))
            {
                fallbackNetworks.Add(network);
            }
        }

        foreach (var range in _options.FallbackIPv6Ranges ?? [])
        {
            if (IPNetwork.TryParse(range, out var network))
            {
                fallbackNetworks.Add(network);
            }
        }

        if (fallbackNetworks.Count > 0)
        {
            lock (_lock)
            {
                _knownNetworks = fallbackNetworks;
            }
            _logger.LogDebug("Initialized with {Count} fallback Cloudflare IP ranges", fallbackNetworks.Count);
        }
    }
}
