using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Options for configuring the audit writer service.
/// </summary>
public sealed class AuditWriterOptions
{
    /// <summary>
    /// Number of records to batch before writing to database.
    /// Default is 100 records.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Interval for time-based flush (future enhancement).
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Interface for DbContexts that support audit record storage.
/// </summary>
/// <remarks>
/// Services that want HTTP audit logging should implement this interface on their DbContext:
/// <code>
/// public class MyDbContext : DbContext, IAuditDbContext
/// {
///     public DbSet&lt;ApiAuditRecord&gt; ApiAuditRecords { get; set; } = null!;
///
///     Task&lt;int&gt; IAuditDbContext.SaveChangesAsync(CancellationToken ct)
///         =&gt; SaveChangesAsync(ct);
/// }
/// </code>
/// </remarks>
public interface IAuditDbContext
{
    /// <summary>
    /// The DbSet for storing API audit records.
    /// </summary>
    DbSet<ApiAuditRecord> ApiAuditRecords { get; }

    /// <summary>
    /// Saves changes to the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entities written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Background service that drains the audit queue and batch-writes to the database.
/// </summary>
/// <typeparam name="TContext">The DbContext type that implements <see cref="IAuditDbContext"/>.</typeparam>
/// <remarks>
/// <para>
/// This service reads from <see cref="IAuditQueue"/> and batch-inserts records to the database.
/// It runs in the background, allowing the middleware to queue records without blocking.
/// </para>
/// <para>
/// <b>Batch behavior:</b> Records are accumulated until <see cref="AuditWriterOptions.BatchSize"/>
/// is reached, then flushed to the database. On shutdown, remaining records are drained.
/// </para>
/// <para>
/// <b>Error handling:</b> Failed batches are logged but not retried. This is an intentional
/// tradeoff - audit is not transactional with the request, and records lost on failure
/// are an acceptable compromise for non-blocking writes.
/// </para>
/// </remarks>
public sealed class AuditWriterService<TContext> : BackgroundService
    where TContext : DbContext, IAuditDbContext
{
    private readonly IAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditWriterService<TContext>> _logger;
    private readonly int _batchSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditWriterService{TContext}"/> class.
    /// </summary>
    public AuditWriterService(
        IAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditWriterService<TContext>> logger,
        IOptions<AuditWriterOptions> options)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchSize = options.Value.BatchSize;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ApiAuditRecord>(_batchSize);

        try
        {
            await foreach (var record in _queue.ReadAllAsync(stoppingToken))
            {
                batch.Add(record);

                if (batch.Count >= _batchSize)
                {
                    await FlushBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown - continue to drain remaining
        }

        // Drain remaining records on shutdown (use CancellationToken.None to ensure completion)
        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    /// <summary>
    /// Flushes a batch of records to the database.
    /// </summary>
    private async Task FlushBatchAsync(List<ApiAuditRecord> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            db.ApiAuditRecords.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);

            AuditMessages.LogAuditBatchWritten(_logger, batch.Count);
        }
        catch (Exception ex)
        {
            AuditMessages.LogAuditBatchFailed(_logger, batch.Count, ex);
            // Records are lost - acceptable tradeoff per design
            // Do not rethrow - continue processing next batch
        }
    }
}
