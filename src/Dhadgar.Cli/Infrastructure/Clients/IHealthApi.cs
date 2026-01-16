using Refit;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Lightweight health check client for /healthz endpoints.
/// </summary>
public interface IHealthApi
{
    [Get("/healthz")]
    Task<HealthStatusResponse?> GetHealthAsync(CancellationToken ct = default);
}

public sealed class HealthStatusResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("service")]
    public string? Service { get; set; }
}
