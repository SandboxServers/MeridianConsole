using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Nodes.Data;

/// <summary>
/// Shared Npgsql configuration for <see cref="NodesDbContext"/>, used by the host and by tests.
/// </summary>
public static class NodesDatabase
{
    /// <summary>Configures the context to use Postgres with <see cref="NodesRetryingExecutionStrategy"/>.</summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.ExecutionStrategy(dependencies => new NodesRetryingExecutionStrategy(dependencies)));
}
