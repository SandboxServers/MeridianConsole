using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dhadgar.Agent.Windows.IPC;

/// <summary>
/// Base class for all pipe messages.
/// </summary>
/// <remarks>
/// SECURITY: All messages are validated on receipt. Message size is limited to prevent DoS.
/// The Type field uses a closed enum to prevent arbitrary command execution.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OutputMessage), "output")]
[JsonDerivedType(typeof(StatusMessage), "status")]
[JsonDerivedType(typeof(CommandMessage), "command")]
[JsonDerivedType(typeof(HeartbeatMessage), "heartbeat")]
[JsonDerivedType(typeof(AcknowledgeMessage), "ack")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(ShutdownMessage), "shutdown")]
[JsonDerivedType(typeof(InputMessage), "input")]
public abstract record PipeMessage
{
    /// <summary>
    /// Maximum allowed message size (256 KB) to prevent memory exhaustion.
    /// </summary>
    public const int MaxMessageSize = 256 * 1024;

    /// <summary>
    /// Maximum allowed output line length (64 KB) per line.
    /// </summary>
    public const int MaxOutputLineLength = 64 * 1024;

    /// <summary>
    /// The server identifier this message relates to.
    /// </summary>
    [JsonPropertyName("serverId")]
    public required string ServerId { get; init; }

    /// <summary>
    /// Timestamp when the message was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional correlation ID for request/response tracking.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Output from the game server process (stdout/stderr).
/// </summary>
/// <remarks>
/// Sent from GameServerWrapper to Agent.
/// </remarks>
public sealed record OutputMessage : PipeMessage
{
    /// <summary>
    /// The output data. Truncated to MaxOutputLineLength.
    /// </summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>
    /// True if this is stderr output, false for stdout.
    /// </summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

/// <summary>
/// Status update from the game server wrapper.
/// </summary>
/// <remarks>
/// Sent from GameServerWrapper to Agent.
/// </remarks>
public sealed record StatusMessage : PipeMessage
{
    /// <summary>
    /// The current state of the game server process.
    /// </summary>
    [JsonPropertyName("state")]
    public required GameServerState State { get; init; }

    /// <summary>
    /// OS process ID if the game server is running.
    /// </summary>
    [JsonPropertyName("osPid")]
    public int? OsPid { get; init; }

    /// <summary>
    /// Exit code if the process has exited.
    /// </summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Current CPU usage percentage (0-100).
    /// </summary>
    [JsonPropertyName("cpuPercent")]
    public double? CpuPercent { get; init; }

    /// <summary>
    /// Current memory usage in bytes.
    /// </summary>
    [JsonPropertyName("memoryBytes")]
    public long? MemoryBytes { get; init; }
}

/// <summary>
/// Command sent from Agent to GameServerWrapper.
/// </summary>
/// <remarks>
/// Sent from Agent to GameServerWrapper.
/// </remarks>
public sealed record CommandMessage : PipeMessage
{
    /// <summary>
    /// The command to execute.
    /// </summary>
    [JsonPropertyName("command")]
    public required GameServerCommand Command { get; init; }

    /// <summary>
    /// Optional payload for the command.
    /// </summary>
    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    /// <summary>
    /// Timeout for the command execution.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Input to be sent to the game server's stdin.
/// </summary>
/// <remarks>
/// Sent from Agent to GameServerWrapper.
/// </remarks>
public sealed record InputMessage : PipeMessage
{
    /// <summary>
    /// The input text to write to stdin.
    /// </summary>
    [JsonPropertyName("input")]
    public required string Input { get; init; }
}

/// <summary>
/// Heartbeat message to verify connection is alive.
/// </summary>
/// <remarks>
/// Can be sent in either direction.
/// </remarks>
public sealed record HeartbeatMessage : PipeMessage
{
    /// <summary>
    /// Sequence number for heartbeat tracking.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }
}

/// <summary>
/// Acknowledgment of a received message.
/// </summary>
public sealed record AcknowledgeMessage : PipeMessage
{
    /// <summary>
    /// The correlation ID of the message being acknowledged.
    /// </summary>
    [JsonPropertyName("acknowledgedId")]
    public required string AcknowledgedId { get; init; }

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Error message for protocol or processing errors.
/// </summary>
public sealed record ErrorMessage : PipeMessage
{
    /// <summary>
    /// Error code for categorization.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Whether this error is fatal and the connection should be closed.
    /// </summary>
    [JsonPropertyName("isFatal")]
    public bool IsFatal { get; init; }
}

/// <summary>
/// Graceful shutdown request.
/// </summary>
/// <remarks>
/// Sent from Agent to GameServerWrapper to initiate graceful shutdown.
/// </remarks>
public sealed record ShutdownMessage : PipeMessage
{
    /// <summary>
    /// Timeout for graceful shutdown before force kill.
    /// </summary>
    [JsonPropertyName("gracefulTimeoutSeconds")]
    public int GracefulTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Reason for the shutdown.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// State of a game server process.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameServerState>))]
public enum GameServerState
{
    /// <summary>
    /// Wrapper is starting up, game server not yet launched.
    /// </summary>
    Initializing,

    /// <summary>
    /// Game server process is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// Game server process is running.
    /// </summary>
    Running,

    /// <summary>
    /// Game server is being stopped gracefully.
    /// </summary>
    Stopping,

    /// <summary>
    /// Game server process has stopped cleanly.
    /// </summary>
    Stopped,

    /// <summary>
    /// Game server process has failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Game server is restarting.
    /// </summary>
    Restarting
}

/// <summary>
/// Commands that can be sent to a game server wrapper.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameServerCommand>))]
public enum GameServerCommand
{
    /// <summary>
    /// Request current status.
    /// </summary>
    GetStatus,

    /// <summary>
    /// Start the game server process.
    /// </summary>
    Start,

    /// <summary>
    /// Stop the game server gracefully.
    /// </summary>
    Stop,

    /// <summary>
    /// Force kill the game server.
    /// </summary>
    Kill,

    /// <summary>
    /// Restart the game server.
    /// </summary>
    Restart,

    /// <summary>
    /// Update resource limits.
    /// </summary>
    UpdateLimits
}

/// <summary>
/// JSON serialization options for pipe protocol.
/// </summary>
public static class PipeProtocolSerializer
{
    /// <summary>
    /// JSON options configured for pipe protocol serialization.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32, // Prevent stack overflow from deeply nested JSON
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serializes a message to JSON bytes.
    /// </summary>
    /// <param name="message">The message to serialize.</param>
    /// <returns>UTF-8 encoded JSON bytes.</returns>
    public static byte[] Serialize(PipeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, Options);
    }

    /// <summary>
    /// Deserializes a message from JSON bytes.
    /// </summary>
    /// <param name="data">UTF-8 encoded JSON bytes.</param>
    /// <returns>The deserialized message, or null if deserialization fails.</returns>
    public static PipeMessage? Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > PipeMessage.MaxMessageSize)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PipeMessage>(data, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deserializes a message from a JSON string.
    /// </summary>
    /// <param name="json">JSON string.</param>
    /// <returns>The deserialized message, or null if deserialization fails.</returns>
    public static PipeMessage? Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        // Check byte length, not character count, since UTF-8 can have multi-byte chars
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        if (byteCount > PipeMessage.MaxMessageSize)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PipeMessage>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
