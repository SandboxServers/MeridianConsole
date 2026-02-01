using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Options for configuring the audit queue capacity.
/// </summary>
public sealed class AuditQueueOptions
{
    /// <summary>
    /// Maximum number of audit records that can be queued.
    /// Default is 10,000 records. When full, writers will wait (backpressure).
    /// </summary>
    public int Capacity { get; set; } = 10_000;
}

/// <summary>
/// Non-blocking queue for audit records using System.Threading.Channels.
/// </summary>
/// <remarks>
/// <para>
/// This queue provides async-friendly, non-blocking queueing of audit records.
/// The middleware queues records here, and the <see cref="AuditWriterService{TContext}"/>
/// drains them in batches to the database.
/// </para>
/// <para>
/// Key characteristics:
/// <list type="bullet">
///   <item>Bounded capacity with backpressure (wait when full)</item>
///   <item>Single reader (one background service)</item>
///   <item>Multiple writers (concurrent request middleware)</item>
///   <item>Zero allocation for fast path</item>
/// </list>
/// </para>
/// </remarks>
// CA1711: "Queue" suffix is semantically accurate and follows Microsoft's documented pattern
// https://learn.microsoft.com/en-us/dotnet/core/extensions/queue-service
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public interface IAuditQueue
{
    /// <summary>
    /// Queues an audit record for background processing.
    /// </summary>
    /// <param name="record">The audit record to queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the record is queued.</returns>
    /// <remarks>
    /// This method will wait if the queue is full (backpressure).
    /// For fire-and-forget in middleware, discard the returned task.
    /// </remarks>
    ValueTask QueueAsync(ApiAuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all queued audit records asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop reading.</param>
    /// <returns>An async enumerable of audit records.</returns>
    /// <remarks>
    /// This should only be called by the <see cref="AuditWriterService{TContext}"/>.
    /// The enumerable completes when the channel is completed and empty.
    /// </remarks>
    IAsyncEnumerable<ApiAuditRecord> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks the queue as complete, preventing further writes.
    /// Called during graceful shutdown.
    /// </summary>
    void Complete();
}

/// <summary>
/// Channel-based implementation of <see cref="IAuditQueue"/>.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public sealed class AuditQueue : IAuditQueue
{
    private readonly Channel<ApiAuditRecord> _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditQueue"/> class.
    /// </summary>
    /// <param name="options">Queue configuration options.</param>
    public AuditQueue(IOptions<AuditQueueOptions> options)
    {
        _channel = Channel.CreateBounded<ApiAuditRecord>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                // Wait when full - provides backpressure
                FullMode = BoundedChannelFullMode.Wait,
                // Only one BackgroundService reads
                SingleReader = true,
                // Multiple middleware instances can write concurrently
                SingleWriter = false
            });
    }

    /// <inheritdoc />
    public ValueTask QueueAsync(ApiAuditRecord record, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(record, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ApiAuditRecord> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public void Complete()
        => _channel.Writer.Complete();
}
