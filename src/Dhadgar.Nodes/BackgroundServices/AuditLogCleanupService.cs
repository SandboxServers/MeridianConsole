using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.BackgroundServices;

/// <summary>
/// Background service that periodically removes old audit logs based on retention policy.
/// </summary>
public sealed class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogCleanupService> _logger;
    private readonly int _retentionDays;
    private readonly TimeSpan _checkInterval;
    private readonly int _batchSize;

    public AuditLogCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<NodesOptions> options,
        ILogger<AuditLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retentionDays = options.Value.AuditLogRetentionDays;
        _checkInterval = TimeSpan.FromHours(options.Value.AuditLogCleanupIntervalHours);
        _batchSize = options.Value.AuditLogCleanupBatchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Audit log cleanup service started. Retention: {RetentionDays} days, Interval: {Interval}",
            _retentionDays,
            _checkInterval);

        // Initial delay to avoid running immediately on startup
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldLogsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audit log cleanup");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Audit log cleanup service stopped");
    }

    private async Task CleanupOldLogsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NodesDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var cutoffDate = timeProvider.GetUtcNow().UtcDateTime.AddDays(-_retentionDays);
        var totalDeleted = 0;

        _logger.LogDebug(
            "Starting audit log cleanup. Deleting logs older than {CutoffDate}",
            cutoffDate);

        // Delete in batches to avoid locking the table for too long
        while (!ct.IsCancellationRequested)
        {
            // Get IDs to delete in this batch
            var idsToDelete = await dbContext.AuditLogs
                .Where(a => a.Timestamp < cutoffDate)
                .OrderBy(a => a.Timestamp)
                .Take(_batchSize)
                .Select(a => a.Id)
                .ToListAsync(ct);

            if (idsToDelete.Count == 0)
            {
                break;
            }

            // Delete the batch
            var deleted = await dbContext.AuditLogs
                .Where(a => idsToDelete.Contains(a.Id))
                .ExecuteDeleteAsync(ct);

            totalDeleted += deleted;

            _logger.LogDebug(
                "Deleted {BatchCount} audit logs in this batch, {TotalCount} total",
                deleted,
                totalDeleted);

            // Small delay between batches to reduce database load
            if (idsToDelete.Count == _batchSize)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Audit log cleanup completed. Deleted {Count} logs older than {CutoffDate}",
                totalDeleted,
                cutoffDate);
        }
    }
}
