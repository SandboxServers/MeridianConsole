using System.ComponentModel.DataAnnotations;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Summary view of a node for list responses.
/// </summary>
public sealed record NodeSummary(
    Guid Id,
    string Name,
    string? DisplayName,
    NodeStatus Status,
    string Platform,
    DateTime? LastHeartbeat);

/// <summary>
/// Detailed view of a node including hardware, health, and capacity.
/// </summary>
public sealed record NodeDetail(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? DisplayName,
    NodeStatus Status,
    string? AgentVersion,
    string Platform,
    DateTime? LastHeartbeat,
    DateTime CreatedAt,
    IReadOnlyList<string> Tags,
    NodeHardwareDto? Hardware,
    NodeHealthDto? Health,
    NodeCapacityDto? Capacity);

/// <summary>
/// Hardware specification data.
/// </summary>
public sealed record NodeHardwareDto(
    string Hostname,
    string? OsVersion,
    int CpuCores,
    long MemoryBytes,
    long DiskBytes,
    DateTime CollectedAt);

/// <summary>
/// Health metrics data.
/// </summary>
public sealed record NodeHealthDto(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    int ActiveGameServers,
    int HealthScore,
    string HealthTrend,
    DateTime? LastScoreChange,
    DateTime ReportedAt);

/// <summary>
/// Capacity tracking data.
/// </summary>
public sealed record NodeCapacityDto(
    int MaxGameServers,
    int CurrentGameServers,
    long AvailableMemoryBytes,
    long AvailableDiskBytes,
    DateTime UpdatedAt);

/// <summary>
/// Request to update a node's properties.
/// </summary>
public sealed record UpdateNodeRequest(
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-z0-9][a-z0-9\-]*[a-z0-9]$|^[a-z0-9]$", ErrorMessage = "Name must be lowercase alphanumeric with optional hyphens, cannot start or end with hyphen")]
    string? Name,

    [StringLength(100, ErrorMessage = "DisplayName cannot exceed 100 characters")]
    string? DisplayName);

/// <summary>
/// Request to update a node's tags.
/// </summary>
public sealed record UpdateNodeTagsRequest(
    [MaxLength(20, ErrorMessage = "Maximum 20 tags allowed")]
    List<string> Tags)
{
    /// <summary>
    /// Validates and normalizes tags (lowercase, trimmed, unique).
    /// </summary>
    public List<string> GetNormalizedTags()
    {
        if (Tags is null || Tags.Count == 0)
        {
            return [];
        }

        return Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length <= 50) // Max tag length
            .Distinct()
            .Take(20) // Max 20 tags
            .ToList();
    }
}
