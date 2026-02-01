using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Health;

namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Interface for communication with the control plane via SignalR.
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
public sealed class TelemetryPayload
{
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
}

/// <summary>
/// A single telemetry event.
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>
    /// Event name/type.
    /// </summary>
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
    /// </summary>
    public Dictionary<string, object?> Properties { get; init; } = [];
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
