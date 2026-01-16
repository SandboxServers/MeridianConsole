using Refit;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Gateway Service (health checks and diagnostics)
/// </summary>
public interface IGatewayApi
{
    [Get("/healthz")]
    Task<HealthResponse> GetHealthAsync(CancellationToken ct = default);

    [Get("/health/{service}")]
    Task<ServiceHealthResponse> GetServiceHealthAsync(string service, CancellationToken ct = default);

    [Get("/readyz")]
    Task<ReadinessResponse> GetReadinessAsync(CancellationToken ct = default);

    [Get("/diagnostics/services")]
    Task<AllServicesHealthResponse> GetAllServicesHealthAsync(CancellationToken ct = default);

    [Get("/diagnostics/routes")]
    Task<RoutesInfoResponse> GetRoutesAsync(CancellationToken ct = default);

    [Get("/diagnostics/clusters")]
    Task<ClustersInfoResponse> GetClustersAsync(CancellationToken ct = default);
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("services")]
    public Dictionary<string, ServiceStatus> Services { get; set; } = new();
}

public class ServiceStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("responseTime")]
    public int ResponseTime { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ServiceHealthResponse
{
    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("responseTime")]
    public int ResponseTime { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ReadinessResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; } = new();
}

public class AllServicesHealthResponse
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("services")]
    public Dictionary<string, ServiceHealthResult> Services { get; set; } = new();
}

public class ServiceHealthResult
{
    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("responseTimeMs")]
    public long ResponseTimeMs { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class RoutesInfoResponse
{
    [JsonPropertyName("routes")]
    public List<RouteInfo> Routes { get; set; } = new();
}

public class RouteInfo
{
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("authorizationPolicy")]
    public string? AuthorizationPolicy { get; set; }

    [JsonPropertyName("rateLimiterPolicy")]
    public string? RateLimiterPolicy { get; set; }

    [JsonPropertyName("order")]
    public int? Order { get; set; }
}

public class ClustersInfoResponse
{
    [JsonPropertyName("clusters")]
    public List<ClusterInfo> Clusters { get; set; } = new();
}

public class ClusterInfo
{
    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("availableDestinations")]
    public int AvailableDestinations { get; set; }

    [JsonPropertyName("totalDestinations")]
    public int TotalDestinations { get; set; }

    [JsonPropertyName("healthStatus")]
    public string HealthStatus { get; set; } = string.Empty;
}
