using System.Linq.Expressions;
using System.Reflection;
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
            var comparison = Expression.Equal(propertyAccess, nullConstant);
            var lambda = Expression.Lambda(comparison, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
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
    /// the <c>UpdatedAt</c> timestamp on modified entities.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A task that represents the asynchronous save operation. The task result contains
    /// the number of state entries written to the database.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This override automatically sets the <c>UpdatedAt</c> property to <see cref="DateTime.UtcNow"/>
    /// for any tracked entities in the <c>Modified</c> state that have an <c>UpdatedAt</c> property.
    /// </para>
    /// <para>
    /// This works for both entities extending <see cref="BaseEntity"/> and those implementing
    /// <see cref="IAuditableEntity"/>.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            // Handle entities implementing IAuditableEntity
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                auditableEntity.UpdatedAt = now;
                continue;
            }

            // Handle entities with UpdatedAt property via reflection (for those not implementing the interface)
            var updatedAtProperty = entry.Entity.GetType().GetProperty("UpdatedAt");
            if (updatedAtProperty != null && updatedAtProperty.PropertyType == typeof(DateTime?))
            {
                updatedAtProperty.SetValue(entry.Entity, now);
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Saves all changes made in this context to the database, automatically updating
    /// the <c>UpdatedAt</c> timestamp on modified entities.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    /// <remarks>
    /// This override automatically sets the <c>UpdatedAt</c> property to <see cref="DateTime.UtcNow"/>
    /// for any tracked entities in the <c>Modified</c> state that have an <c>UpdatedAt</c> property.
    /// </remarks>
    public override int SaveChanges()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            // Handle entities implementing IAuditableEntity
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                auditableEntity.UpdatedAt = now;
                continue;
            }

            // Handle entities with UpdatedAt property via reflection (for those not implementing the interface)
            var updatedAtProperty = entry.Entity.GetType().GetProperty("UpdatedAt");
            if (updatedAtProperty != null && updatedAtProperty.PropertyType == typeof(DateTime?))
            {
                updatedAtProperty.SetValue(entry.Entity, now);
            }
        }

        return base.SaveChanges();
    }
}
