using Refit;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Dhadgar.Cli.Infrastructure.Clients;

/// <summary>
/// Type-safe Refit interface for Nodes Service API calls
/// </summary>
public interface INodesApi
{
    // ========================================================================
    // Node Management (User-facing)
    // ========================================================================

    [Get("/organizations/{orgId}/nodes")]
    Task<PagedNodesResponse> GetNodesAsync(
        string orgId,
        [AliasAs("skip")] int? skip = null,
        [AliasAs("take")] int? take = null,
        [AliasAs("status")] string? status = null,
        CancellationToken ct = default);

    [Get("/organizations/{orgId}/nodes/{nodeId}")]
    Task<NodeDetailResponse> GetNodeAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Patch("/organizations/{orgId}/nodes/{nodeId}")]
    Task<NodeDetailResponse> UpdateNodeAsync(
        string orgId,
        string nodeId,
        [Body] UpdateNodeRequest request,
        CancellationToken ct = default);

    [Delete("/organizations/{orgId}/nodes/{nodeId}")]
    Task DecommissionNodeAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Post("/organizations/{orgId}/nodes/{nodeId}/maintenance")]
    Task<NodeDetailResponse> EnterMaintenanceAsync(string orgId, string nodeId, CancellationToken ct = default);

    [Delete("/organizations/{orgId}/nodes/{nodeId}/maintenance")]
    Task<NodeDetailResponse> ExitMaintenanceAsync(string orgId, string nodeId, CancellationToken ct = default);

    // ========================================================================
    // Enrollment Token Management (User-facing)
    // ========================================================================

    [Post("/organizations/{orgId}/enrollment/tokens")]
    Task<CreateEnrollmentTokenResponse> CreateEnrollmentTokenAsync(
        string orgId,
        [Body] CreateEnrollmentTokenRequest request,
        CancellationToken ct = default);

    [Get("/organizations/{orgId}/enrollment/tokens")]
    Task<Collection<EnrollmentTokenResponse>> GetEnrollmentTokensAsync(
        string orgId,
        CancellationToken ct = default);

    [Delete("/organizations/{orgId}/enrollment/tokens/{tokenId}")]
    Task RevokeEnrollmentTokenAsync(string orgId, string tokenId, CancellationToken ct = default);
}

// ============================================================================
// Response DTOs
// ============================================================================

public class PagedNodesResponse
{
    [JsonPropertyName("items")]
    public Collection<NodeSummaryResponse> Items { get; set; } = [];

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("skip")]
    public int Skip { get; set; }

    [JsonPropertyName("take")]
    public int Take { get; set; }
}

public class NodeSummaryResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("lastHeartbeat")]
    public DateTimeOffset? LastHeartbeat { get; set; }
}

public class NodeDetailResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("agentVersion")]
    public string? AgentVersion { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("lastHeartbeat")]
    public DateTimeOffset? LastHeartbeat { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("hardware")]
    public NodeHardwareResponse? Hardware { get; set; }

    [JsonPropertyName("health")]
    public NodeHealthResponse? Health { get; set; }

    [JsonPropertyName("capacity")]
    public NodeCapacityResponse? Capacity { get; set; }
}

public class NodeHardwareResponse
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("cpuCores")]
    public int CpuCores { get; set; }

    [JsonPropertyName("memoryBytes")]
    public long MemoryBytes { get; set; }

    [JsonPropertyName("diskBytes")]
    public long DiskBytes { get; set; }

    [JsonPropertyName("collectedAt")]
    public DateTime CollectedAt { get; set; }
}

public class NodeHealthResponse
{
    [JsonPropertyName("cpuUsagePercent")]
    public double CpuUsagePercent { get; set; }

    [JsonPropertyName("memoryUsagePercent")]
    public double MemoryUsagePercent { get; set; }

    [JsonPropertyName("diskUsagePercent")]
    public double DiskUsagePercent { get; set; }

    [JsonPropertyName("activeGameServers")]
    public int ActiveGameServers { get; set; }

    [JsonPropertyName("healthIssues")]
    public Collection<string>? HealthIssues { get; set; }

    [JsonPropertyName("reportedAt")]
    public DateTime ReportedAt { get; set; }
}

public class NodeCapacityResponse
{
    [JsonPropertyName("maxGameServers")]
    public int MaxGameServers { get; set; }

    [JsonPropertyName("currentGameServers")]
    public int CurrentGameServers { get; set; }

    [JsonPropertyName("availableMemoryBytes")]
    public long AvailableMemoryBytes { get; set; }

    [JsonPropertyName("availableDiskBytes")]
    public long AvailableDiskBytes { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// ============================================================================
// Request DTOs
// ============================================================================

public class UpdateNodeRequest
{
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

public class CreateEnrollmentTokenRequest
{
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("expiresInMinutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExpiresInMinutes { get; set; }
}

public class CreateEnrollmentTokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

public class EnrollmentTokenResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("usedAt")]
    public DateTime? UsedAt { get; set; }

    [JsonPropertyName("usedByNodeId")]
    public string? UsedByNodeId { get; set; }

    [JsonPropertyName("createdByUserId")]
    public string CreatedByUserId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; set; }
}
