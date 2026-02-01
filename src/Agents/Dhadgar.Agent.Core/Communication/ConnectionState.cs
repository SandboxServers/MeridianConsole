namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// Represents the current state of the control plane connection.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Not connected to the control plane.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Attempting to establish connection.
    /// </summary>
    Connecting,

    /// <summary>
    /// Connected and operational.
    /// </summary>
    Connected,

    /// <summary>
    /// Connection lost, attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Fatal error, manual intervention required.
    /// </summary>
    Failed
}

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// The previous connection state.
    /// </summary>
    public ConnectionState PreviousState { get; }

    /// <summary>
    /// The current connection state.
    /// </summary>
    public ConnectionState CurrentState { get; }

    /// <summary>
    /// Error message if the state change was due to an error.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Time of the state change.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    public ConnectionStateChangedEventArgs(
        ConnectionState previousState,
        ConnectionState currentState,
        string? error = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = error;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
