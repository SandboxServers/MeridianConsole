namespace Dhadgar.Notifications.Alerting;

/// <summary>
/// Severity levels for alert messages.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Warning level - something unexpected happened but service continues.</summary>
    Warning,

    /// <summary>Error level - an error occurred that may affect service functionality.</summary>
    Error,

    /// <summary>Critical level - a critical error requiring immediate attention.</summary>
    Critical
}

/// <summary>
/// Represents an alert message to be dispatched to notification channels.
/// </summary>
public sealed record AlertMessage
{
    /// <summary>Gets the alert title (short summary).</summary>
    public required string Title { get; init; }

    /// <summary>Gets the detailed alert message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the severity level.</summary>
    public required AlertSeverity Severity { get; init; }

    /// <summary>Gets the service that generated the alert.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Gets the trace ID for distributed tracing correlation.</summary>
    public string? TraceId { get; init; }

    /// <summary>Gets the correlation ID for request tracking.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the exception type if the alert was triggered by an exception.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Gets the timestamp when the alert was created.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets additional context data.</summary>
    public IReadOnlyDictionary<string, string>? AdditionalData { get; init; }
}
