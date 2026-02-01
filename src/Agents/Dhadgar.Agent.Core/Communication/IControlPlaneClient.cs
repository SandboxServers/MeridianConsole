using System.ComponentModel.DataAnnotations;
using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Health;

namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Interface for communication with the control plane via SignalR.
/// <para>
/// <strong>SECURITY REQUIREMENT:</strong> All implementations of IControlPlaneClient MUST use mutual TLS (mTLS).
/// Implementations must:
/// <list type="bullet">
///   <item>Present a valid client certificate during TLS handshake</item>
///   <item>Validate the server certificate chain against trusted CA</item>
///   <item>Enforce hostname verification</item>
///   <item>Refuse any non-mTLS connections</item>
/// </list>
/// This is a hard security requirement and not optional.
/// </para>
/// </summary>
public interface IControlPlaneClient
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when a command is received from the control plane.
    /// </summary>
    event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    /// <summary>
    /// Connect to the control plane.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the control plane.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a heartbeat with current status to the control plane.
    /// </summary>
    /// <param name="payload">Heartbeat payload with system metrics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send command execution result back to the control plane.
    /// </summary>
    /// <param name="result">Command execution result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendCommandResultAsync(CommandResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send telemetry data to the control plane.
    /// </summary>
    /// <param name="payload">Telemetry payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendTelemetryAsync(TelemetryPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for command received from control plane.
/// </summary>
public sealed class CommandReceivedEventArgs : EventArgs
{
    /// <summary>
    /// The received command envelope.
    /// </summary>
    public CommandEnvelope Command { get; }

    /// <summary>
    /// Time when the command was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; }

    public CommandReceivedEventArgs(CommandEnvelope command)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        ReceivedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Telemetry payload for sending metrics and events.
/// </summary>
public sealed class TelemetryPayload : IValidatableObject
{
    /// <summary>Maximum number of metrics per payload.</summary>
    public const int MaxMetrics = 500;

    /// <summary>Maximum number of events per payload.</summary>
    public const int MaxEvents = 100;

    /// <summary>Maximum length for metric/property keys.</summary>
    public const int MaxKeyLength = 256;

    /// <summary>
    /// Node identifier.
    /// </summary>
    public required Guid NodeId { get; init; }

    /// <summary>
    /// Timestamp of telemetry collection.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Custom metrics dictionary.
    /// </summary>
    public Dictionary<string, double> Metrics { get; init; } = [];

    /// <summary>
    /// Events that occurred since last telemetry submission.
    /// </summary>
    public IList<TelemetryEvent> Events { get; init; } = [];

    /// <summary>
    /// Validates the telemetry payload to prevent resource exhaustion.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate NodeId is set
        if (NodeId == Guid.Empty)
        {
            yield return new ValidationResult(
                "NodeId cannot be empty",
                [nameof(NodeId)]);
        }

        if (Metrics.Count > MaxMetrics)
        {
            yield return new ValidationResult(
                $"Metrics count ({Metrics.Count}) exceeds maximum ({MaxMetrics})",
                [nameof(Metrics)]);
        }

        if (Events.Count > MaxEvents)
        {
            yield return new ValidationResult(
                $"Events count ({Events.Count}) exceeds maximum ({MaxEvents})",
                [nameof(Events)]);
        }

        foreach (var key in Metrics.Keys)
        {
            if (key.Length > MaxKeyLength)
            {
                yield return new ValidationResult(
                    $"Metric key length ({key.Length}) exceeds maximum ({MaxKeyLength})",
                    [nameof(Metrics)]);
                break;
            }
        }

        foreach (var evt in Events)
        {
            foreach (var result in evt.Validate())
            {
                yield return result;
            }
        }
    }
}

/// <summary>
/// A single telemetry event.
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>Maximum number of properties per event.</summary>
    public const int MaxProperties = 50;

    /// <summary>Maximum length for string property values.</summary>
    public const int MaxStringValueLength = 1024;

    /// <summary>
    /// Allowed primitive types for property values.
    /// Complex types are rejected to prevent payload bloat and deserialization issues.
    /// </summary>
    private static readonly HashSet<Type> AllowedPropertyTypes =
    [
        typeof(string),
        typeof(bool),
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(Guid)
    ];

    /// <summary>
    /// Event name/type.
    /// </summary>
    [MaxLength(TelemetryPayload.MaxKeyLength)]
    public required string Name { get; init; }

    /// <summary>
    /// Event timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Event severity level.
    /// </summary>
    public TelemetryEventLevel Level { get; init; } = TelemetryEventLevel.Information;

    /// <summary>
    /// Event properties/data.
    /// Only primitive types are allowed (string, bool, numeric types, DateTime, Guid, enums).
    /// </summary>
    public Dictionary<string, object?> Properties { get; init; } = [];

    /// <summary>
    /// Validates the telemetry event.
    /// </summary>
    internal IEnumerable<ValidationResult> Validate()
    {
        if (Name.Length > TelemetryPayload.MaxKeyLength)
        {
            yield return new ValidationResult(
                $"Event name length ({Name.Length}) exceeds maximum ({TelemetryPayload.MaxKeyLength})",
                [nameof(Name)]);
        }

        if (Properties.Count > MaxProperties)
        {
            yield return new ValidationResult(
                $"Event properties count ({Properties.Count}) exceeds maximum ({MaxProperties})",
                [nameof(Properties)]);
        }

        foreach (var (key, value) in Properties)
        {
            if (key.Length > TelemetryPayload.MaxKeyLength)
            {
                yield return new ValidationResult(
                    $"Property key length ({key.Length}) exceeds maximum ({TelemetryPayload.MaxKeyLength})",
                    [nameof(Properties)]);
                break;
            }

            if (value is string strValue && strValue.Length > MaxStringValueLength)
            {
                yield return new ValidationResult(
                    $"Property value length ({strValue.Length}) exceeds maximum ({MaxStringValueLength})",
                    [nameof(Properties)]);
                break;
            }

            // Validate property value types - only allow primitives
            if (value is not null)
            {
                var valueType = value.GetType();
                var isAllowedType = AllowedPropertyTypes.Contains(valueType) ||
                                    valueType.IsEnum ||
                                    Nullable.GetUnderlyingType(valueType) is { } underlying &&
                                    (AllowedPropertyTypes.Contains(underlying) || underlying.IsEnum);

                if (!isAllowedType)
                {
                    yield return new ValidationResult(
                        $"Property '{key}' has disallowed type '{valueType.Name}'. Only primitive types are allowed.",
                        [nameof(Properties)]);
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Telemetry event severity levels.
/// </summary>
public enum TelemetryEventLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
