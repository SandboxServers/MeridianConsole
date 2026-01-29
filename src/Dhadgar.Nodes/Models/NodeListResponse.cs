using Dhadgar.Nodes.Data.Entities;

namespace Dhadgar.Nodes.Models;

/// <summary>
/// Enhanced summary view of a node for list responses with additional fields.
/// </summary>
public sealed record NodeListItem(
    Guid Id,
    string Name,
    string? DisplayName,
    NodeStatus Status,
    string Platform,
    DateTime? LastHeartbeat,
    DateTime CreatedAt,
    int? HealthScore,
    int ActiveServers,
    IReadOnlyList<string> Tags);

/// <summary>
/// Paginated response with filter metadata.
/// </summary>
/// <typeparam name="T">Type of items in the collection</typeparam>
public sealed record FilteredPagedResponse<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;

    /// <summary>
    /// Metadata about applied filters.
    /// </summary>
    public required FilterMetadata Filters { get; init; }
}

/// <summary>
/// Metadata about what filters were applied to a query.
/// </summary>
public sealed record FilterMetadata
{
    public NodeStatus? Status { get; init; }
    public string? Platform { get; init; }
    public int? MinHealthScore { get; init; }
    public int? MaxHealthScore { get; init; }
    public bool? HasActiveServers { get; init; }
    public string? Search { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Field used for sorting. Default: "name".
    /// Note: This default must match NodeListQuery.SortBy default.
    /// </summary>
    public string SortBy { get; init; } = "name";

    /// <summary>
    /// Sort direction. Default: "asc".
    /// Note: This default must match NodeListQuery.SortOrder default.
    /// </summary>
    public string SortOrder { get; init; } = "asc";

    public bool IncludeDecommissioned { get; init; }
}

/// <summary>
/// Factory for creating filtered paged responses.
/// </summary>
public static class FilteredPagedResponse
{
    public static FilteredPagedResponse<T> Create<T>(
        IReadOnlyCollection<T> items,
        int total,
        NodeListQuery query)
    {
        var tags = query.ParseTagsFilter();

        return new FilteredPagedResponse<T>
        {
            Items = items,
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            Total = total,
            Filters = new FilterMetadata
            {
                Status = query.Status,
                Platform = query.Platform,
                MinHealthScore = query.MinHealthScore,
                MaxHealthScore = query.MaxHealthScore,
                HasActiveServers = query.HasActiveServers,
                Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
                Tags = tags.Count > 0 ? tags : null,
                SortBy = query.SortBy,
                SortOrder = query.SortOrder,
                IncludeDecommissioned = query.IncludeDecommissioned
            }
        };
    }
}
