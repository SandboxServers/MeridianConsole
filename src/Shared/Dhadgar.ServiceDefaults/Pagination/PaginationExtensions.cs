using Dhadgar.Contracts;

namespace Dhadgar.ServiceDefaults.Pagination;

/// <summary>
/// Extension methods for applying pagination to collections.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Applies pagination to a collection and returns a paged response.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="items">The full collection of items.</param>
    /// <param name="page">Page number (1-based, defaults to 1).</param>
    /// <param name="pageSize">Items per page (defaults to 50). Value is clamped to 1-100 by <see cref="PaginationRequest.NormalizedPageSize"/>.</param>
    /// <returns>A paged response containing the requested page of items.</returns>
    public static PagedResponse<T> ToPagedResponse<T>(
        this IReadOnlyCollection<T> items,
        int? page = null,
        int? pageSize = null)
    {
        var pagination = new PaginationRequest
        {
            Page = page ?? 1,
            PageSize = pageSize ?? 50
        };

        return items.ToPagedResponse(pagination);
    }

    /// <summary>
    /// Applies pagination to a collection and returns a paged response.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="items">The full collection of items.</param>
    /// <param name="pagination">The pagination request parameters.</param>
    /// <returns>A paged response containing the requested page of items.</returns>
    public static PagedResponse<T> ToPagedResponse<T>(
        this IReadOnlyCollection<T> items,
        PaginationRequest pagination)
    {
        var pagedItems = items
            .Skip(pagination.Skip)
            .Take(pagination.NormalizedPageSize)
            .ToArray();

        return PagedResponse<T>.Create(pagedItems, items.Count, pagination);
    }

    /// <summary>
    /// Applies pagination to an enumerable and returns a paged response.
    /// Note: This will enumerate the source twice (once for count, once for items).
    /// Prefer using the IReadOnlyCollection overload when possible.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="items">The enumerable of items.</param>
    /// <param name="page">Page number (1-based, defaults to 1).</param>
    /// <param name="pageSize">Items per page (defaults to 50). Value is clamped to 1-100 by <see cref="PaginationRequest.NormalizedPageSize"/>.</param>
    /// <returns>A paged response containing the requested page of items.</returns>
    public static PagedResponse<T> ToPagedResponse<T>(
        this IEnumerable<T> items,
        int? page = null,
        int? pageSize = null)
    {
        var list = items as IReadOnlyCollection<T> ?? items.ToList();
        return list.ToPagedResponse(page, pageSize);
    }
}
