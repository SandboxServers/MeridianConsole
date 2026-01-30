using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Shared.Data;

/// <summary>
/// Base DbContext class providing common functionality for all Dhadgar service database contexts.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides:
/// <list type="bullet">
///   <item><description>Automatic soft-delete query filters for entities with <c>DeletedAt</c> property</description></item>
///   <item><description>Provider-specific handling for PostgreSQL vs InMemory/SQLite</description></item>
///   <item><description>Automatic <c>UpdatedAt</c> timestamp management on entity modifications</description></item>
/// </list>
/// </para>
/// <para>
/// Service-specific DbContexts should inherit from this class and call the protected
/// helper methods in their <c>OnModelCreating</c> override.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ServersDbContext : DhadgarDbContext
/// {
///     public ServersDbContext(DbContextOptions&lt;ServersDbContext&gt; options) : base(options) { }
///
///     public DbSet&lt;GameServer&gt; GameServers =&gt; Set&lt;GameServer&gt;();
///
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///
///         // Apply configurations
///         modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
///
///         // Apply base class conventions
///         ApplySoftDeleteConventions(modelBuilder);
///         ApplyProviderSpecificConventions(modelBuilder);
///     }
/// }
/// </code>
/// </example>
public abstract class DhadgarDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DhadgarDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext.</param>
    protected DhadgarDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the database provider is PostgreSQL.
    /// </summary>
    /// <remarks>
    /// Used to conditionally apply PostgreSQL-specific features like JSONB columns
    /// and xmin-based concurrency tokens.
    /// </remarks>
    protected bool IsPostgreSql =>
        Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
        Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets a value indicating whether the database provider is InMemory or SQLite (test providers).
    /// </summary>
    /// <remarks>
    /// These providers don't support PostgreSQL-specific features and require alternative configurations.
    /// </remarks>
    protected bool IsTestProvider =>
        Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true ||
        Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Applies global query filters to automatically exclude soft-deleted entities.
    /// </summary>
    /// <param name="modelBuilder">The model builder being used to construct the model.</param>
    /// <remarks>
    /// <para>
    /// This method scans all entity types in the model and applies a query filter
    /// (<c>e.DeletedAt == null</c>) to any entity that has a <c>DeletedAt</c> property.
    /// </para>
    /// <para>
    /// If an entity already has a query filter (e.g., for tenant isolation), this method
    /// merges the soft-delete filter with the existing filter using <c>AndAlso</c>.
    /// </para>
    /// <para>
    /// The filter is applied using expression trees to work with any entity type,
    /// including those implementing <see cref="IAuditableEntity"/> or extending <see cref="BaseEntity"/>.
    /// </para>
    /// <para>
    /// Use <c>IgnoreQueryFilters()</c> in queries when you need to include soft-deleted records.
    /// </para>
    /// </remarks>
    protected virtual void ApplySoftDeleteConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var deletedAtProperty = entityType.ClrType.GetProperty("DeletedAt");
            if (deletedAtProperty == null || deletedAtProperty.PropertyType != typeof(DateTime?))
            {
                continue;
            }

            // Build expression: e => e.DeletedAt == null
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var propertyAccess = Expression.Property(parameter, deletedAtProperty);
            var nullConstant = Expression.Constant(null, typeof(DateTime?));
            var softDeleteFilter = Expression.Equal(propertyAccess, nullConstant);

            // Check for existing query filter and merge if present
            // Using pragma to suppress obsolete warning - GetDeclaredQueryFilters returns IQueryFilter
            // which has a different structure than LambdaExpression
#pragma warning disable CS0618 // Type or member is obsolete
            var existingFilter = entityType.GetQueryFilter();
#pragma warning restore CS0618

            LambdaExpression finalLambda;
            if (existingFilter != null)
            {
                // Replace the parameter in the existing filter with our parameter
                var existingBody = new ParameterReplacerVisitor(existingFilter.Parameters[0], parameter)
                    .Visit(existingFilter.Body);

                // Combine: existingFilter AND softDeleteFilter
                var combinedBody = Expression.AndAlso(existingBody, softDeleteFilter);
                finalLambda = Expression.Lambda(combinedBody, parameter);
            }
            else
            {
                finalLambda = Expression.Lambda(softDeleteFilter, parameter);
            }

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(finalLambda);
        }
    }

    /// <summary>
    /// Expression visitor that replaces one parameter with another.
    /// Used to canonicalize parameters when merging query filters.
    /// </summary>
    private sealed class ParameterReplacerVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public ParameterReplacerVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParameter ? _newParameter : base.VisitParameter(node);
        }
    }

    /// <summary>
    /// Applies provider-specific conventions to handle differences between PostgreSQL and test providers.
    /// </summary>
    /// <param name="modelBuilder">The model builder being used to construct the model.</param>
    /// <remarks>
    /// <para>
    /// For non-PostgreSQL providers (InMemory, SQLite), this method:
    /// <list type="bullet">
    ///   <item><description>Converts JSONB column types to standard columns</description></item>
    ///   <item><description>Configures <c>RowVersion</c> as a standard auto-incrementing column instead of using PostgreSQL's xmin</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Derived contexts should call this method at the end of their <c>OnModelCreating</c>
    /// to ensure provider-specific adjustments are applied after entity configurations.
    /// </para>
    /// </remarks>
    protected virtual void ApplyProviderSpecificConventions(ModelBuilder modelBuilder)
    {
        if (!IsTestProvider)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Handle RowVersion property for entities with optimistic concurrency
            var rowVersionProperty = entityType.ClrType.GetProperty("RowVersion");
            if (rowVersionProperty != null && rowVersionProperty.PropertyType == typeof(uint))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property("RowVersion")
                    .IsConcurrencyToken()
                    .HasDefaultValue(0u)
                    .ValueGeneratedOnAddOrUpdate();
            }

            // Handle JSONB columns - remove the column type specification for test providers
            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType();
                if (columnType != null &&
                    (columnType.Equals("jsonb", StringComparison.OrdinalIgnoreCase) ||
                     columnType.Equals("json", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType(null);
                }
            }
        }
    }

    /// <summary>
    /// Saves all changes made in this context to the database, automatically updating
    /// audit timestamps on entities.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous save operation. The task result contains
    /// the number of state entries written to the database.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This override automatically sets audit timestamps for tracked entities implementing
    /// <see cref="IAuditableEntity"/>:
    /// <list type="bullet">
    ///   <item><description><c>CreatedAt</c> is set to the current UTC time for entities in the <c>Added</c> state</description></item>
    ///   <item><description><c>UpdatedAt</c> is set to the current UTC time for entities in the <c>Modified</c> state</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This works for both entities extending <see cref="BaseEntity"/> and those implementing
    /// <see cref="IAuditableEntity"/>.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps(DateTime.UtcNow);
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Saves all changes made in this context to the database, automatically updating
    /// audit timestamps on entities.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    /// <remarks>
    /// <para>
    /// This override automatically sets audit timestamps for tracked entities implementing
    /// <see cref="IAuditableEntity"/>:
    /// <list type="bullet">
    ///   <item><description><c>CreatedAt</c> is set to the current UTC time for entities in the <c>Added</c> state</description></item>
    ///   <item><description><c>UpdatedAt</c> is set to the current UTC time for entities in the <c>Modified</c> state</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public override int SaveChanges()
    {
        ApplyAuditTimestamps(DateTime.UtcNow);
        return base.SaveChanges();
    }

    /// <summary>
    /// Applies audit timestamps to tracked entities based on their state.
    /// </summary>
    /// <param name="now">The timestamp to apply.</param>
    /// <remarks>
    /// <para>
    /// For entities in the <c>Added</c> state, sets <c>CreatedAt</c> to the provided timestamp
    /// and clears <c>UpdatedAt</c> to null.
    /// </para>
    /// <para>
    /// For entities in the <c>Modified</c> state, sets <c>UpdatedAt</c> to the provided timestamp.
    /// </para>
    /// </remarks>
    private void ApplyAuditTimestamps(DateTime now)
    {
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = null;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
