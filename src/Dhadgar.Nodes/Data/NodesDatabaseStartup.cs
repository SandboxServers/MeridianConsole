using Dhadgar.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Nodes.Data;

/// <summary>
/// Applies the Nodes database migrations. Returns a failure instead of throwing, so the startup service can retry.
/// </summary>
public interface INodesDatabaseMigrator
{
    /// <summary>Applies pending migrations. A failure carries an error code and the exception type name only.</summary>
    Task<Result> MigrateAsync(CancellationToken ct);
}

/// <summary>
/// Entity Framework implementation of <see cref="INodesDatabaseMigrator"/>. Creates its own scope per call.
/// </summary>
public sealed class EfNodesDatabaseMigrator(IServiceScopeFactory scopes) : INodesDatabaseMigrator
{
    /// <inheritdoc />
    public async Task<Result> MigrateAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        try
        {
            await db.Database.MigrateAsync(ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the exception type name: messages can carry the host or the user name.
            return Result.Failure($"[Nodes.DatabaseUnavailable] migration failed ({ex.GetType().Name})");
        }
    }
}

/// <summary>
/// Migrates the database in the background in Development, so the host starts while Postgres is still down.
/// Outside Development it never migrates (D-ML232). Readiness is reported by <see cref="NodesDatabaseHealthCheck"/>,
/// which reads the schema itself (D-ML290).
/// </summary>
public sealed class NodesDatabaseStartupService(
    IServiceScopeFactory scopes,
    IHostEnvironment env,
    TimeProvider time,
    ILogger<NodesDatabaseStartupService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!env.IsDevelopment())
        {
            return;
        }

        var delay = InitialBackoff;
        try
        {
            while (true)
            {
                var result = await MigrateOnceAsync(stoppingToken);
                if (result.IsSuccess)
                {
                    logger.LogInformation("Nodes database migrations applied");
                    return;
                }

                logger.LogWarning(
                    "Nodes database migrations not applied; retrying in {DelaySeconds} s. {Error}",
                    delay.TotalSeconds,
                    result.Error);

                await Task.Delay(delay, time, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is stopping: leave quietly.
        }
    }

    private async Task<Result> MigrateOnceAsync(CancellationToken ct)
    {
        // Resolved per attempt inside a scope: the migrator is scoped and this service is a singleton.
        await using var scope = scopes.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<INodesDatabaseMigrator>();
        return await migrator.MigrateAsync(ct);
    }
}

/// <summary>
/// Readiness check for the Nodes database. Reports Unhealthy while migrations are pending (any environment) or the
/// database cannot be reached. Reads the schema on every probe rather than a startup flag, so it stays true after the
/// database is migrated, rolled forward or recreated (D-ML290).
/// </summary>
public sealed class NodesDatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(ProbeTimeout);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NodesDbContext>();

            if (db.Database.IsRelational())
            {
                var pending = await db.Database.GetPendingMigrationsAsync(probeCts.Token);
                if (pending.Any())
                {
                    return HealthCheckResult.Unhealthy("database migrations pending");
                }
            }

            var canConnect = await db.Database.CanConnectAsync(probeCts.Token);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Never the exception text: it can carry the connection string.
            return HealthCheckResult.Unhealthy("database unreachable");
        }
    }
}

/// <summary>
/// Registration for the Nodes database startup: the migrator, the background migration and the <c>nodes-database</c> readiness check.
/// </summary>
public static class NodesDatabaseStartupExtensions
{
    /// <summary>Adds the Nodes database startup migration and its readiness check. Call after <c>AddDbContext&lt;NodesDbContext&gt;</c>.</summary>
    public static IServiceCollection AddNodesDatabaseStartup(this IServiceCollection services)
    {
        services.TryAddScoped<INodesDatabaseMigrator, EfNodesDatabaseMigrator>();
        services.AddHostedService<NodesDatabaseStartupService>();
        services.AddHealthChecks().AddCheck<NodesDatabaseHealthCheck>("nodes-database", tags: ["ready"]);
        return services;
    }
}
