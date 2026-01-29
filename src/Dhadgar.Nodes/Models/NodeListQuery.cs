using System.ComponentModel.DataAnnotations;
using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Query parameters for listing and filtering nodes.
/// </summary>
public sealed class NodeListQuery
{
    /// <summary>Page number (1-based). Default: 1</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1")]
    public int Page { get; set; } = 1;

    /// <summary>Items per page (1-100). Default: 20</summary>
    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; set; } = 20;

    /// <summary>Filter by node status (online, offline, degraded, maintenance, decommissioned, enrolling)</summary>
    public NodeStatus? Status { get; set; }

    /// <summary>Filter by platform (windows, linux)</summary>
    [StringLength(20)]
    public string? Platform { get; set; }

    /// <summary>Minimum health score filter (0-100)</summary>
    [Range(0, 100, ErrorMessage = "MinHealthScore must be between 0 and 100")]
    public int? MinHealthScore { get; set; }

    /// <summary>Maximum health score filter (0-100)</summary>
    [Range(0, 100, ErrorMessage = "MaxHealthScore must be between 0 and 100")]
    public int? MaxHealthScore { get; set; }

    /// <summary>Filter nodes with active servers (true) or without (false)</summary>
    public bool? HasActiveServers { get; set; }

    /// <summary>Full-text search on name and displayName</summary>
    [StringLength(100)]
    public string? Search { get; set; }

    /// <summary>Filter by tags (comma-separated, matches any)</summary>
    [StringLength(500)]
    public string? Tags { get; set; }

    /// <summary>
    /// Field to sort by: name, displayName, status, healthScore, lastHeartbeat, createdAt, activeServers.
    /// Default: name
    /// </summary>
    [RegularExpression("^(name|displayName|status|healthScore|lastHeartbeat|createdAt|activeServers)$",
        ErrorMessage = "SortBy must be one of: name, displayName, status, healthScore, lastHeartbeat, createdAt, activeServers")]
    public string SortBy { get; set; } = "name";

    /// <summary>Sort order: asc or desc. Default: asc</summary>
    [RegularExpression("^(asc|desc)$", ErrorMessage = "SortOrder must be 'asc' or 'desc'")]
    public string SortOrder { get; set; } = "asc";

    /// <summary>Include decommissioned nodes (normally excluded). Default: false</summary>
    public bool IncludeDecommissioned { get; set; }

    // Computed properties
    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
    public int NormalizedPage => Math.Max(1, Page);
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);
    public bool IsAscending => string.Equals(SortOrder, "asc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parse tags string into a normalized list.
    /// Applies same normalization rules as UpdateNodeTagsRequest.GetNormalizedTags():
    /// - Trims whitespace
    /// - Converts to lowercase
    /// - Filters out empty entries
    /// - Truncates tags longer than 50 characters
    /// - Returns at most 20 distinct tags
    /// </summary>
    public IReadOnlyList<string> ParseTagsFilter()
    {
        if (string.IsNullOrWhiteSpace(Tags))
        {
            return Array.Empty<string>();
        }

        return Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length <= 50) // Max tag length (matches GetNormalizedTags)
            .Distinct()
            .Take(20) // Max 20 tags (matches GetNormalizedTags)
            .ToList();
    }
}
