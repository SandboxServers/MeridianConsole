namespace Dhadgar.Contracts;

/// <summary>
/// Standard pagination request parameters.
/// </summary>
public sealed record PaginationRequest
{
    /// <summary>Page number (1-based)</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page (default 50, max 100)</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Sort field</summary>
    public string? Sort { get; init; }

    /// <summary>Sort order (asc or desc)</summary>
    public string Order { get; init; } = "asc";

    /// <summary>Calculate skip count for database query</summary>
    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;

    /// <summary>Normalized page (minimum 1)</summary>
    public int NormalizedPage => Math.Max(1, Page);

    /// <summary>Normalized page size (between 1 and 100)</summary>
    public int NormalizedPageSize => Math.Clamp(PageSize, 1, 100);

    /// <summary>Is ascending order</summary>
    public bool IsAscending => !string.Equals(Order, "desc", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Standard paginated response wrapper.
/// </summary>
/// <typeparam name="T">Type of items in the collection</typeparam>
public sealed record PagedResponse<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;

    /// <summary>
    /// Creates a paged response from items and total count.
    /// </summary>
    // CA1000: Static factory method on generic type is intentional for fluent API usage pattern.
    // Alternative would require a separate non-generic factory class, which is more awkward.
#pragma warning disable CA1000
    public static PagedResponse<T> Create(
        IReadOnlyCollection<T> items,
        int total,
        PaginationRequest pagination)
    {
        return new PagedResponse<T>
        {
            Items = items,
            Page = pagination.NormalizedPage,
            PageSize = pagination.NormalizedPageSize,
            Total = total
        };
    }
#pragma warning restore CA1000
}
