using Microsoft.Extensions.Logging;

namespace Dhadgar.ServiceDefaults.Audit;

/// <summary>
/// Source-generated logging messages for audit infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// EventId ranges for audit logging:
/// <list type="bullet">
///   <item>9200-9209: Batch writing events</item>
///   <item>9210-9219: Cleanup events</item>
///   <item>9220-9229: Queue events</item>
/// </list>
/// </para>
/// <para>
/// All methods are static and accept an <see cref="ILogger"/> parameter to support
/// use from generic background services with different logger categories.
/// </para>
/// </remarks>
public static partial class AuditMessages
{
    /// <summary>
    /// Logs that an audit batch was successfully written to the database.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">Number of records written.</param>
    public static void LogAuditBatchWritten(ILogger logger, int count)
    {
        AuditBatchWritten(logger, count);
    }

    /// <summary>
    /// Logs that an audit batch failed to write to the database.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">Number of records that failed to write.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    public static void LogAuditBatchFailed(ILogger logger, int count, Exception exception)
    {
        AuditBatchFailed(logger, exception, count);
    }

    /// <summary>
    /// Logs that audit cleanup completed successfully.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="count">Number of records deleted.</param>
    /// <param name="cutoff">The cutoff date used for deletion.</param>
    public static void LogAuditCleanupCompleted(ILogger logger, int count, DateTime cutoff)
    {
        AuditCleanupCompleted(logger, count, cutoff);
    }

    /// <summary>
    /// Logs that audit cleanup failed.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    public static void LogAuditCleanupFailed(ILogger logger, Exception exception)
    {
        AuditCleanupFailed(logger, exception);
    }

    /// <summary>
    /// Logs that the audit queue is full (backpressure activated).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public static void LogAuditQueueFull(ILogger logger)
    {
        AuditQueueFull(logger);
    }

    // Source-generated logging methods using EventId range 9200-9229 (Audit)

    [LoggerMessage(
        EventId = 9200,
        Level = LogLevel.Debug,
        Message = "Flushed {Count} audit records to database")]
    private static partial void AuditBatchWritten(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9201,
        Level = LogLevel.Error,
        Message = "Failed to write {Count} audit records to database")]
    private static partial void AuditBatchFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(
        EventId = 9210,
        Level = LogLevel.Information,
        Message = "Audit cleanup completed: {Count} records older than {Cutoff:yyyy-MM-dd} deleted")]
    private static partial void AuditCleanupCompleted(ILogger logger, int count, DateTime cutoff);

    [LoggerMessage(
        EventId = 9211,
        Level = LogLevel.Error,
        Message = "Error during audit cleanup")]
    private static partial void AuditCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 9220,
        Level = LogLevel.Warning,
        Message = "Audit queue is full - backpressure activated")]
    private static partial void AuditQueueFull(ILogger logger);
}
