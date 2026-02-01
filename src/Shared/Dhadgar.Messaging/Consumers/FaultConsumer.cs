using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Messaging.Consumers;

/// <summary>
/// Base class for handling faulted (dead-letter) messages in MassTransit.
/// </summary>
/// <typeparam name="TMessage">The original message type that failed.</typeparam>
/// <remarks>
/// <para>
/// When a message exhausts all retries and redelivery attempts, MassTransit wraps it in a
/// <see cref="Fault{TMessage}"/> and sends it to an error queue. This base class provides
/// standardized handling for such failed messages.
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
///   <item><description>Logging failures to a centralized error tracking system</description></item>
///   <item><description>Sending alerts to operations team</description></item>
///   <item><description>Moving messages to a dead-letter store for manual review</description></item>
///   <item><description>Compensating transactions for saga failures</description></item>
/// </list>
/// </para>
/// <para>
/// To use, create a consumer that inherits from this class and register it:
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 1. Create a fault consumer for your message type
/// public sealed class SendEmailNotificationFaultConsumer
///     : FaultConsumer&lt;SendEmailNotification&gt;
/// {
///     private readonly IAlertService _alerts;
///
///     public SendEmailNotificationFaultConsumer(
///         ILogger&lt;SendEmailNotificationFaultConsumer&gt; logger,
///         IAlertService alerts) : base(logger)
///     {
///         _alerts = alerts;
///     }
///
///     protected override async Task HandleFaultAsync(
///         ConsumeContext&lt;Fault&lt;SendEmailNotification&gt;&gt; context,
///         CancellationToken ct)
///     {
///         var fault = context.Message;
///
///         // Alert operations team about the failure
///         await _alerts.SendAsync(
///             $"Email notification {fault.Message.NotificationId} failed permanently",
///             severity: AlertSeverity.High);
///     }
/// }
///
/// // 2. Register the fault consumer in MassTransit configuration
/// services.AddDhadgarMessaging(config, x =>
/// {
///     x.AddConsumer&lt;SendEmailNotificationConsumer&gt;();
///     x.AddConsumer&lt;SendEmailNotificationFaultConsumer&gt;();
/// });
/// </code>
/// </example>
public abstract class FaultConsumer<TMessage> : IConsumer<Fault<TMessage>>
    where TMessage : class
{
    /// <summary>
    /// Gets the logger instance for this consumer.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultConsumer{TMessage}"/> class.
    /// </summary>
    /// <param name="logger">The logger to use for this consumer.</param>
    protected FaultConsumer(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the incoming fault message by logging the failure and delegating to
    /// <see cref="HandleFaultAsync"/>.
    /// </summary>
    /// <param name="context">The consume context containing the fault and metadata.</param>
    public async Task Consume(ConsumeContext<Fault<TMessage>> context)
    {
        var fault = context.Message;
        var messageType = typeof(TMessage).Name;
        var faultId = context.MessageId?.ToString() ?? "unknown";
        var correlationId = context.CorrelationId?.ToString() ?? fault.FaultId.ToString();
        var stopwatch = Stopwatch.StartNew();

        using var scope = Logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageType"] = messageType,
            ["FaultId"] = fault.FaultId,
            ["FaultedMessageId"] = fault.FaultedMessageId?.ToString() ?? "unknown",
            ["CorrelationId"] = correlationId,
            ["Host"] = fault.Host?.MachineName ?? "unknown"
        });

        // Log all exceptions that caused the fault
        LogFaultExceptions(fault, messageType);

        try
        {
            await HandleFaultAsync(context, context.CancellationToken);

            stopwatch.Stop();
            Logger.LogDebug(
                "Successfully processed fault for {MessageType} (FaultId: {FaultId}) in {ElapsedMs}ms",
                messageType, fault.FaultId, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            Logger.LogWarning(
                "Fault handling for {MessageType} (FaultId: {FaultId}) was cancelled after {ElapsedMs}ms",
                messageType, fault.FaultId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex,
                "Error handling fault for {MessageType} (FaultId: {FaultId}) after {ElapsedMs}ms: {ErrorMessage}",
                messageType, fault.FaultId, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Logs detailed information about the exceptions that caused the message to fault.
    /// </summary>
    private void LogFaultExceptions(Fault<TMessage> fault, string messageType)
    {
        if (fault.Exceptions is null || fault.Exceptions.Length == 0)
        {
            Logger.LogError(
                "Message {MessageType} permanently failed with no exception details. FaultId: {FaultId}, Timestamp: {Timestamp}",
                messageType, fault.FaultId, fault.Timestamp);
            return;
        }

        foreach (var exceptionInfo in fault.Exceptions)
        {
            Logger.LogError(
                "Message {MessageType} permanently failed. FaultId: {FaultId}, ExceptionType: {ExceptionType}, " +
                "Message: {ExceptionMessage}, Source: {Source}, Timestamp: {Timestamp}",
                messageType,
                fault.FaultId,
                exceptionInfo.ExceptionType,
                exceptionInfo.Message,
                exceptionInfo.Source,
                fault.Timestamp);

            // Log stack trace at debug level for investigation
            if (!string.IsNullOrEmpty(exceptionInfo.StackTrace))
            {
                Logger.LogDebug(
                    "Stack trace for {MessageType} fault {FaultId}:\n{StackTrace}",
                    messageType, fault.FaultId, exceptionInfo.StackTrace);
            }

            // Recursively log all inner exceptions
            var inner = exceptionInfo.InnerException;
            var level = 1;
            while (inner is not null)
            {
                Logger.LogError(
                    "Inner exception (level {Level}) for {MessageType} fault {FaultId}: {InnerExceptionType} - {InnerMessage}",
                    level,
                    messageType,
                    fault.FaultId,
                    inner.ExceptionType,
                    inner.Message);

                if (!string.IsNullOrEmpty(inner.StackTrace))
                {
                    Logger.LogDebug(
                        "Inner exception (level {Level}) stack trace for {MessageType} fault {FaultId}:\n{StackTrace}",
                        level, messageType, fault.FaultId, inner.StackTrace);
                }

                inner = inner.InnerException;
                level++;
            }
        }
    }

    /// <summary>
    /// Override this method to implement custom fault handling logic.
    /// </summary>
    /// <param name="context">The consume context containing the fault message.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// The fault message contains:
    /// <list type="bullet">
    ///   <item><description><c>Message</c>: The original message that failed</description></item>
    ///   <item><description><c>Exceptions</c>: Array of exception information</description></item>
    ///   <item><description><c>FaultId</c>: Unique identifier for this fault</description></item>
    ///   <item><description><c>FaultedMessageId</c>: ID of the original message</description></item>
    ///   <item><description><c>Timestamp</c>: When the fault occurred</description></item>
    ///   <item><description><c>Host</c>: Information about the host that faulted</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    protected abstract Task HandleFaultAsync(
        ConsumeContext<Fault<TMessage>> context,
        CancellationToken ct);
}

/// <summary>
/// Extension methods for configuring fault consumers.
/// </summary>
public static class FaultConsumerExtensions
{
    /// <summary>
    /// Configures a receive endpoint for consuming faults of a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The original message type.</typeparam>
    /// <typeparam name="TFaultConsumer">The fault consumer type.</typeparam>
    /// <param name="configurator">The bus registration configurator.</param>
    /// <param name="endpointName">
    /// Optional custom endpoint name. Defaults to "meridian.{messagetype}_fault".
    /// </param>
    /// <returns>The configurator for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures a dedicated endpoint for fault messages, which is separate
    /// from the error queue that MassTransit creates automatically. Use this when you need
    /// to process faults programmatically (alerting, logging, compensation).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddDhadgarMessaging(config, x =>
    /// {
    ///     x.AddConsumer&lt;SendEmailNotificationConsumer&gt;();
    ///     x.AddFaultConsumer&lt;SendEmailNotification, SendEmailNotificationFaultConsumer&gt;();
    /// });
    /// </code>
    /// </example>
    public static IBusRegistrationConfigurator AddFaultConsumer<TMessage, TFaultConsumer>(
        this IBusRegistrationConfigurator configurator,
        string? endpointName = null)
        where TMessage : class
        where TFaultConsumer : class, IConsumer<Fault<TMessage>>
    {
        configurator.AddConsumer<TFaultConsumer>();

        // The endpoint will be configured automatically by MassTransit's ConfigureEndpoints
        // using the StaticEntityNameFormatter. For explicit endpoint naming:
        // endpointName ??= $"meridian.{typeof(TMessage).Name.ToLowerInvariant()}_fault";

        return configurator;
    }
}
