namespace Dhadgar.Discord.Services;

/// <summary>
/// Health status for a single microservice.
/// </summary>
public record ServiceHealthStatus(
    string serviceName,
    Uri? url,
    bool isHealthy,
    int? responseTimeMs,
    string? error);

/// <summary>
/// Aggregated health status for the entire platform.
/// </summary>
public record PlatformHealthStatus(
    IReadOnlyList<ServiceHealthStatus> services,
    int healthyCount,
    int unhealthyCount,
    DateTimeOffset checkedAtUtc);

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

        var healthyCount = results.Count(r => r.isHealthy);
        var unhealthyCount = results.Count(r => !r.isHealthy);

        _logger.LogInformation(
            "Platform health check complete: {Healthy}/{Total} services healthy",
            healthyCount, results.Length);

        return new PlatformHealthStatus(
            services: results.OrderBy(s => s.serviceName).ToList(),
            healthyCount: healthyCount,
            unhealthyCount: unhealthyCount,
            checkedAtUtc: DateTimeOffset.UtcNow);
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

        // Parse URL to Uri for type safety
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var serviceUri);

        try
        {
            using var response = await _httpClient.GetAsync(healthUrl, ct);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ServiceHealthStatus(
                    serviceName: serviceName,
                    url: serviceUri,
                    isHealthy: true,
                    responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                    error: null);
            }

            return new ServiceHealthStatus(
                serviceName: serviceName,
                url: serviceUri,
                isHealthy: false,
                responseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                error: $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller requested cancellation - rethrow to propagate
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout (not caller cancellation)
            return new ServiceHealthStatus(
                serviceName: serviceName,
                url: serviceUri,
                isHealthy: false,
                responseTimeMs: null,
                error: "Timeout");
        }
        catch (HttpRequestException ex)
        {
            return new ServiceHealthStatus(
                serviceName: serviceName,
                url: serviceUri,
                isHealthy: false,
                responseTimeMs: null,
                error: ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking health of {Service}", serviceName);

            return new ServiceHealthStatus(
                serviceName: serviceName,
                url: serviceUri,
                isHealthy: false,
                responseTimeMs: null,
                error: ex.Message);
        }
    }
}
