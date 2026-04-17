using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Options for configuring the audit cleanup service.
/// </summary>
public sealed class AuditCleanupOptions
{
    /// <summary>
    /// How often to run cleanup.
    /// Default is 24 hours.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Retention period for audit records.
    /// Records older than this are deleted.
    /// Default is 90 days (per AUDIT-04).
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Batch size for deletion to avoid long-running transactions.
    /// Default is 10,000 records.
    /// </summary>
    public int BatchSize { get; set; } = 10_000;

    /// <summary>
    /// Whether the cleanup service is enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Background service that periodically cleans up old audit records.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IAuditDbContext"/>.</typeparam>
/// <remarks>
/// <para>
/// This service enforces the 90-day retention policy (per AUDIT-04) by deleting
/// records older than <see cref="AuditCleanupOptions.RetentionPeriod"/>.
/// </para>
/// <para>
/// <b>Batch deletion:</b> Records are deleted in batches to avoid long-running
/// transactions that could block other database operations. Uses EF Core's
/// <c>ExecuteDeleteAsync</c> which doesn't load entities into memory.
/// </para>
/// <para>
/// Pattern follows <c>TokenCleanupService</c> from Identity service.
/// </para>
/// </remarks>
public sealed class AuditCleanupService<TContext> : BackgroundService
    where TContext : DbContext, IAuditDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditCleanupService<TContext>> _logger;
    private readonly AuditCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditCleanupService{TContext}"/> class.
    /// </summary>
    public AuditCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditCleanupService<TContext>> logger,
        IOptions<AuditCleanupOptions> options,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Audit cleanup service is disabled");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Audit cleanup service started. Interval: {Interval}, RetentionPeriod: {RetentionPeriod}",
                _options.Interval,
                _options.RetentionPeriod);
        }

        // Initial delay to let the service start up (don't run cleanup immediately)
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldRecordsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AuditMessages.LogAuditCleanupFailed(_logger, ex);
            }

            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Audit cleanup service stopped");
    }

    /// <summary>
    /// Deletes audit records older than the retention period.
    /// </summary>
    private async Task CleanupOldRecordsAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _options.RetentionPeriod;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var totalDeleted = 0;
        int deleted;

        // Batch delete to avoid long-running transactions
        do
        {
            // ExecuteDeleteAsync performs server-side delete without loading entities
            deleted = await db.ApiAuditRecords
                .Where(r => r.TimestampUtc < cutoff)
                .Take(_options.BatchSize)
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
        } while (deleted == _options.BatchSize && !cancellationToken.IsCancellationRequested);

        if (totalDeleted > 0)
        {
            AuditMessages.LogAuditCleanupCompleted(_logger, totalDeleted, cutoff);
        }
        else
        {
            _logger.LogDebug("Audit cleanup completed: no records to delete");
        }
    }
}
