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
    /// Sanitized error message if the state change was due to an error.
    /// Note: Error text is sanitized to remove sensitive data (connection strings, tokens, certificates).
    /// Callers should not pass raw secrets in error messages.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Time of the state change.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Creates a new connection state changed event.
    /// </summary>
    /// <param name="previousState">The previous connection state.</param>
    /// <param name="currentState">The new connection state.</param>
    /// <param name="error">Optional error message. Will be sanitized to remove sensitive data.</param>
    public ConnectionStateChangedEventArgs(
        ConnectionState previousState,
        ConnectionState currentState,
        string? error = null)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Error = SanitizeError(error);
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sanitizes error messages to remove sensitive data such as connection strings,
    /// tokens, and certificate information.
    /// </summary>
    private static string? SanitizeError(string? error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return error;
        }

        var sanitized = error;

        // Remove connection strings (various formats)
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(Server|Host|Data Source|Initial Catalog|Database|User Id|Password|Pwd)=[^;]*",
            "$1=[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove bearer tokens
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"Bearer\s+[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]*",
            "Bearer [REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove certificate data (PEM format)
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"-----BEGIN[^-]+-----[\s\S]*?-----END[^-]+-----",
            "[CERTIFICATE REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove base64-encoded data that looks like keys/secrets (long base64 strings)
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"[A-Za-z0-9+/]{64,}={0,2}",
            "[REDACTED]");

        return sanitized;
    }
}
