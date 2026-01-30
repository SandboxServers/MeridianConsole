namespace Dhadgar.Shared.Data;

/// <summary>
/// Provides extension methods for <see cref="IQueryable{T}"/> to support common query patterns
/// such as filtering, pagination, and ordering for entities implementing <see cref="IAuditableEntity"/>.
/// </summary>
/// <remarks>
/// These extensions are designed to work with EF Core queries but can also be used
/// with any LINQ-compatible query provider or in-memory collections.
/// </remarks>
public static class QueryableExtensions
{
    /// <summary>
    /// The maximum allowed page size to prevent excessive data retrieval.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The default page size when not specified.
    /// </summary>
    public const int DefaultPageSize = 10;

    /// <summary>
    /// Filters the query to include only active (non-deleted) entities.
    /// </summary>
    /// <typeparam name="T">The entity type, which must implement <see cref="IAuditableEntity"/>.</typeparam>
    /// <param name="query">The queryable source to filter.</param>
    /// <returns>A queryable containing only entities where <see cref="IAuditableEntity.DeletedAt"/> is null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <example>
    /// <code>
    /// var activeUsers = dbContext.Users.Active().ToList();
    /// </code>
    /// </example>
    public static IQueryable<T> Active<T>(this IQueryable<T> query) where T : class, IAuditableEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.Where(e => e.DeletedAt == null);
    }

    /// <summary>
    /// Applies pagination to the query with bounds checking to ensure valid page and page size values.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable source to paginate.</param>
    /// <param name="page">
    /// The 1-based page number. Values less than 1 are clamped to 1.
    /// </param>
    /// <param name="pageSize">
    /// The number of items per page. Values are clamped to the range [1, <see cref="MaxPageSize"/>].
    /// Defaults to <see cref="DefaultPageSize"/>.
    /// </param>
    /// <returns>A queryable with Skip and Take applied for the requested page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method ensures safe pagination by clamping input values to valid ranges:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Page numbers less than 1 are treated as page 1.</description></item>
    /// <item><description>Page sizes less than 1 are treated as 1.</description></item>
    /// <item><description>Page sizes greater than <see cref="MaxPageSize"/> (100) are clamped to 100.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get the second page of 20 items
    /// var page2 = dbContext.Users.Paginate(page: 2, pageSize: 20).ToList();
    ///
    /// // Uses defaults: page 1, 10 items
    /// var firstPage = dbContext.Users.Paginate().ToList();
    /// </code>
    /// </example>
    public static IQueryable<T> Paginate<T>(this IQueryable<T> query, int page = 1, int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(query);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Use long arithmetic to avoid overflow with large page values
        long offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            offset = int.MaxValue;
        }

        return query.Skip((int)offset).Take(pageSize);
    }

    /// <summary>
    /// Orders the query by <see cref="IAuditableEntity.CreatedAt"/> in descending order (newest first).
    /// </summary>
    /// <typeparam name="T">The entity type, which must implement <see cref="IAuditableEntity"/>.</typeparam>
    /// <param name="query">The queryable source to order.</param>
    /// <returns>An ordered queryable with the most recently created entities first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <example>
    /// <code>
    /// // Get the 10 most recent users
    /// var recentUsers = dbContext.Users.OrderByNewest().Take(10).ToList();
    ///
    /// // Combine with Active() and Paginate()
    /// var activePage = dbContext.Users
    ///     .Active()
    ///     .OrderByNewest()
    ///     .Paginate(page: 1, pageSize: 25)
    ///     .ToList();
    /// </code>
    /// </example>
    public static IQueryable<T> OrderByNewest<T>(this IQueryable<T> query) where T : class, IAuditableEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.OrderByDescending(e => e.CreatedAt);
    }

    /// <summary>
    /// Orders the query by <see cref="IAuditableEntity.CreatedAt"/> in ascending order (oldest first).
    /// </summary>
    /// <typeparam name="T">The entity type, which must implement <see cref="IAuditableEntity"/>.</typeparam>
    /// <param name="query">The queryable source to order.</param>
    /// <returns>An ordered queryable with the oldest entities first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <example>
    /// <code>
    /// // Get all users ordered from oldest to newest
    /// var oldestFirst = dbContext.Users.OrderByOldest().ToList();
    /// </code>
    /// </example>
    public static IQueryable<T> OrderByOldest<T>(this IQueryable<T> query) where T : class, IAuditableEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.OrderBy(e => e.CreatedAt);
    }
}
