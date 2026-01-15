namespace Dhadgar.Discord.Services;

/// <summary>
/// Health status for a single microservice.
/// </summary>
public record ServiceHealthStatus(
    string ServiceName,
    string Url,
    bool IsHealthy,
    int? ResponseTimeMs,
    string? Error);

/// <summary>
/// Aggregated health status for the entire platform.
/// </summary>
public record PlatformHealthStatus(
    IReadOnlyList<ServiceHealthStatus> Services,
    int HealthyCount,
    int UnhealthyCount,
    DateTimeOffset CheckedAtUtc);

/// <summary>
/// Service that checks health of all platform microservices.
/// </summary>
public interface IPlatformHealthService
{
    Task<PlatformHealthStatus> CheckAllServicesAsync(CancellationToken ct = default);
}

/// <summary>
/// Checks health endpoints of all Meridian Console microservices.
/// </summary>
public sealed class PlatformHealthService : IPlatformHealthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformHealthService> _logger;

    // Default service URLs for local development
    private static readonly Dictionary<string, string> DefaultServiceUrls = new()
    {
        ["Gateway"] = "http://localhost:5000",
        ["Identity"] = "http://localhost:5001",
        ["Billing"] = "http://localhost:5002",
        ["Servers"] = "http://localhost:5003",
        ["Nodes"] = "http://localhost:5004",
        ["Tasks"] = "http://localhost:5005",
        ["Files"] = "http://localhost:5006",
        ["Mods"] = "http://localhost:5007",
        ["Console"] = "http://localhost:5008",
        ["Notifications"] = "http://localhost:5009",
        ["Firewall"] = "http://localhost:5010",
        ["Secrets"] = "http://localhost:5080",
        ["Discord"] = "http://localhost:5012"
    };

    public PlatformHealthService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PlatformHealthService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PlatformHealthStatus> CheckAllServicesAsync(CancellationToken ct = default)
    {
        var serviceUrls = GetServiceUrls();
        var checkTasks = serviceUrls.Select(kvp => CheckServiceAsync(kvp.Key, kvp.Value, ct));
        var results = await Task.WhenAll(checkTasks);

        var healthyCount = results.Count(r => r.IsHealthy);
        var unhealthyCount = results.Count(r => !r.IsHealthy);

        _logger.LogInformation(
            "Platform health check complete: {Healthy}/{Total} services healthy",
            healthyCount, results.Length);

        return new PlatformHealthStatus(
            Services: results.OrderBy(s => s.ServiceName).ToList(),
            HealthyCount: healthyCount,
            UnhealthyCount: unhealthyCount,
            CheckedAtUtc: DateTimeOffset.UtcNow);
    }

    private Dictionary<string, string> GetServiceUrls()
    {
        // Check for configured URLs, fall back to defaults
        var configuredUrls = _configuration.GetSection("Services").Get<Dictionary<string, string>>();

        if (configuredUrls is not null && configuredUrls.Count > 0)
        {
            // Merge with defaults (configured takes precedence)
            var merged = new Dictionary<string, string>(DefaultServiceUrls);
            foreach (var kvp in configuredUrls)
            {
                merged[kvp.Key] = kvp.Value;
            }
            return merged;
        }

        return DefaultServiceUrls;
    }

    private async Task<ServiceHealthStatus> CheckServiceAsync(
        string serviceName,
        string baseUrl,
        CancellationToken ct)
    {
        var healthUrl = $"{baseUrl.TrimEnd('/')}/healthz";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _httpClient.GetAsync(healthUrl, ct);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ServiceHealthStatus(
                    ServiceName: serviceName,
                    Url: baseUrl,
                    IsHealthy: true,
                    ResponseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                    Error: null);
            }

            return new ServiceHealthStatus(
                ServiceName: serviceName,
                Url: baseUrl,
                IsHealthy: false,
                ResponseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                Error: $"HTTP {(int)response.StatusCode}");
        }
        catch (TaskCanceledException)
        {
            return new ServiceHealthStatus(
                ServiceName: serviceName,
                Url: baseUrl,
                IsHealthy: false,
                ResponseTimeMs: null,
                Error: "Timeout");
        }
        catch (HttpRequestException ex)
        {
            return new ServiceHealthStatus(
                ServiceName: serviceName,
                Url: baseUrl,
                IsHealthy: false,
                ResponseTimeMs: null,
                Error: ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking health of {Service}", serviceName);

            return new ServiceHealthStatus(
                ServiceName: serviceName,
                Url: baseUrl,
                IsHealthy: false,
                ResponseTimeMs: null,
                Error: ex.Message);
        }
    }
}
