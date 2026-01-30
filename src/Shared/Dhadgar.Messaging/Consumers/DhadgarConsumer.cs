using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Messaging.Consumers;

/// <summary>
/// Base class for MassTransit consumers that provides standardized logging, correlation context,
/// and error handling for the Dhadgar platform.
/// </summary>
/// <typeparam name="TMessage">The message type this consumer handles.</typeparam>
/// <remarks>
/// <para>
/// Derived consumers should override <see cref="ConsumeAsync"/> to implement business logic.
/// The base class handles:
/// <list type="bullet">
///   <item><description>Correlation ID extraction and logging scope</description></item>
///   <item><description>Standardized error logging</description></item>
///   <item><description>Execution timing metrics</description></item>
/// </list>
/// </para>
/// <para>
/// For simple consumers that don't need these features, implement <see cref="IConsumer{TMessage}"/>
/// directly instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ServerProvisionedConsumer : DhadgarConsumer&lt;ServerProvisioned&gt;
/// {
///     private readonly INotificationService _notifications;
///
///     public ServerProvisionedConsumer(
///         ILogger&lt;ServerProvisionedConsumer&gt; logger,
///         INotificationService notifications) : base(logger)
///     {
///         _notifications = notifications;
///     }
///
///     protected override async Task ConsumeAsync(
///         ConsumeContext&lt;ServerProvisioned&gt; context,
///         CancellationToken ct)
///     {
///         await _notifications.SendAsync(
///             context.Message.OwnerId,
///             $"Server {context.Message.ServerName} is now ready!");
///     }
/// }
/// </code>
/// </example>
public abstract class DhadgarConsumer<TMessage> : IConsumer<TMessage>
    where TMessage : class
{
    /// <summary>
    /// Gets the logger instance for this consumer.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DhadgarConsumer{TMessage}"/> class.
    /// </summary>
    /// <param name="logger">The logger to use for this consumer.</param>
    protected DhadgarConsumer(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the incoming message by setting up logging context and delegating to
    /// <see cref="ConsumeAsync"/>.
    /// </summary>
    /// <param name="context">The consume context containing the message and metadata.</param>
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        var messageType = typeof(TMessage).Name;
        var messageId = context.MessageId?.ToString() ?? "unknown";
        var correlationId = context.CorrelationId?.ToString() ?? context.MessageId?.ToString() ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        using var scope = Logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageType"] = messageType,
            ["MessageId"] = messageId,
            ["CorrelationId"] = correlationId,
            ["ConversationId"] = context.ConversationId?.ToString() ?? "none"
        });

        Logger.LogDebug("Consuming {MessageType} message {MessageId}", messageType, messageId);

        try
        {
            await ConsumeAsync(context, context.CancellationToken);

            stopwatch.Stop();
            Logger.LogDebug("Successfully consumed {MessageType} message {MessageId} in {ElapsedMs}ms",
                messageType, messageId, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            Logger.LogWarning("Consumption of {MessageType} message {MessageId} was cancelled after {ElapsedMs}ms",
                messageType, messageId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex,
                "Error consuming {MessageType} message {MessageId} after {ElapsedMs}ms: {ErrorMessage}",
                messageType, messageId, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Processes the message. Override this method to implement consumer logic.
    /// </summary>
    /// <param name="context">The consume context containing the message and metadata.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task ConsumeAsync(ConsumeContext<TMessage> context, CancellationToken ct);
}
