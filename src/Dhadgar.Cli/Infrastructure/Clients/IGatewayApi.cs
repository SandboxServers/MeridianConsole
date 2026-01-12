using Refit;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Gateway Service (health checks)
/// </summary>
public interface IGatewayApi
{
    [Get("/healthz")]
    Task<HealthResponse> GetHealthAsync(CancellationToken ct = default);

    [Get("/health/{service}")]
    Task<ServiceHealthResponse> GetServiceHealthAsync(string service, CancellationToken ct = default);
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
