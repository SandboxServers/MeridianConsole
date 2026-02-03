using System.Buffers;
using System.IO.Pipes;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.IPC;

/// <summary>
/// Represents a named pipe connection from a game server wrapper.
/// </summary>
/// <remarks>
/// SECURITY: Each connection is authenticated by pipe security ACLs.
/// Only the specific game server's Virtual Service Account can connect.
/// </remarks>
internal sealed class PipeClientConnection : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipeStream;
    private readonly string _serverId;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _connectionCts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile bool _disposed;
    private long _heartbeatSequence;

    /// <summary>
    /// Maximum buffer size for reading messages.
    /// </summary>
    private const int MaxBufferSize = PipeMessage.MaxMessageSize + 4; // +4 for length prefix

    /// <summary>
    /// Event raised when a message is received from this connection.
    /// </summary>
    public event EventHandler<PipeMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Event raised when the connection is disconnected.
    /// </summary>
    public event EventHandler<PipeDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Gets the server ID for this connection.
    /// </summary>
    public string ServerId => _serverId;

    /// <summary>
    /// Gets whether the connection is still active.
    /// </summary>
    public bool IsConnected => !_disposed && _pipeStream.IsConnected;

    /// <summary>
    /// Gets when this connection was established.
    /// </summary>
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>
    /// Gets when the last message was received.
    /// </summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeClientConnection"/> class.
    /// </summary>
    /// <param name="pipeStream">The connected pipe stream.</param>
    /// <param name="serverId">The server ID this connection is for.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Time provider for timestamps.</param>
    public PipeClientConnection(
        NamedPipeServerStream pipeStream,
        string serverId,
        ILogger logger,
        TimeProvider timeProvider)
    {
        _pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
        _serverId = serverId ?? throw new ArgumentNullException(nameof(serverId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        ConnectedAt = _timeProvider.GetUtcNow();
        LastMessageAt = ConnectedAt;
    }

    /// <summary>
    /// Starts reading messages from the pipe.
    /// </summary>
    /// <param name="cancellationToken">External cancellation token.</param>
    public async Task StartReadingAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _connectionCts.Token);

        var buffer = ArrayPool<byte>.Shared.Rent(MaxBufferSize);

        try
        {
            while (!linkedCts.Token.IsCancellationRequested && _pipeStream.IsConnected)
            {
                try
                {
                    // Read the 4-byte length prefix
                    var lengthBytesRead = await ReadExactAsync(buffer.AsMemory(0, 4), linkedCts.Token)
                        .ConfigureAwait(false);

                    if (lengthBytesRead == 0)
                    {
                        // Connection closed
                        _logger.LogDebug("Pipe connection closed for server {ServerId}", _serverId);
                        break;
                    }

                    if (lengthBytesRead < 4)
                    {
                        _logger.LogWarning(
                            "Incomplete length prefix from server {ServerId}, got {Bytes} bytes",
                            _serverId, lengthBytesRead);
                        break;
                    }

                    var messageLength = BitConverter.ToInt32(buffer, 0);

                    // Validate message length
                    if (messageLength <= 0 || messageLength > PipeMessage.MaxMessageSize)
                    {
                        _logger.LogWarning(
                            "Invalid message length {Length} from server {ServerId}",
                            messageLength, _serverId);

                        // Send error and continue
                        await SendErrorAsync(
                            "INVALID_LENGTH",
                            $"Message length {messageLength} exceeds maximum of {PipeMessage.MaxMessageSize}",
                            isFatal: false,
                            linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Read the message body
                    var messageBytesRead = await ReadExactAsync(
                        buffer.AsMemory(0, messageLength),
                        linkedCts.Token).ConfigureAwait(false);

                    if (messageBytesRead < messageLength)
                    {
                        _logger.LogWarning(
                            "Incomplete message from server {ServerId}, expected {Expected} got {Actual}",
                            _serverId, messageLength, messageBytesRead);
                        break;
                    }

                    // Deserialize the message
                    var message = PipeProtocolSerializer.Deserialize(buffer.AsSpan(0, messageLength));

                    if (message is null)
                    {
                        _logger.LogWarning("Failed to deserialize message from server {ServerId}", _serverId);
                        await SendErrorAsync(
                            "INVALID_MESSAGE",
                            "Failed to deserialize message",
                            isFatal: false,
                            linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    // Update last message time
                    LastMessageAt = _timeProvider.GetUtcNow();

                    // Raise event
                    try
                    {
                        MessageReceived?.Invoke(this, new PipeMessageReceivedEventArgs
                        {
                            ServerId = _serverId,
                            Message = message,
                            ReceivedAt = LastMessageAt
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in message handler for server {ServerId}", _serverId);
                    }
                }
                catch (IOException ex) when (!linkedCts.Token.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "IO error reading from server {ServerId}", _serverId);
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            OnDisconnected();
        }
    }

    /// <summary>
    /// Sends a message to the game server wrapper.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> SendMessageAsync(PipeMessage message, CancellationToken cancellationToken)
    {
        if (_disposed || !_pipeStream.IsConnected)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(message);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageBytes = PipeProtocolSerializer.Serialize(message);
            var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);

            // Write length prefix + message
            await _pipeStream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            await _pipeStream.WriteAsync(messageBytes, cancellationToken).ConfigureAwait(false);
            await _pipeStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "IO error writing to server {ServerId}", _serverId);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends a command to the game server wrapper.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="payload">Optional command payload.</param>
    /// <param name="timeoutSeconds">Command timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendCommandAsync(
        GameServerCommand command,
        string? payload = null,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        var message = new CommandMessage
        {
            ServerId = _serverId,
            Timestamp = _timeProvider.GetUtcNow(),
            Command = command,
            Payload = payload,
            TimeoutSeconds = timeoutSeconds,
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends input to the game server's stdin.
    /// </summary>
    /// <param name="input">The input text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        var message = new InputMessage
        {
            ServerId = _serverId,
            Timestamp = _timeProvider.GetUtcNow(),
            Input = input
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends a heartbeat to verify the connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var message = new HeartbeatMessage
        {
            ServerId = _serverId,
            Timestamp = _timeProvider.GetUtcNow(),
            Sequence = Interlocked.Increment(ref _heartbeatSequence)
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends a shutdown request to the game server wrapper.
    /// </summary>
    /// <param name="gracefulTimeoutSeconds">Timeout for graceful shutdown.</param>
    /// <param name="reason">Shutdown reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendShutdownAsync(
        int gracefulTimeoutSeconds = 30,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var message = new ShutdownMessage
        {
            ServerId = _serverId,
            Timestamp = _timeProvider.GetUtcNow(),
            GracefulTimeoutSeconds = gracefulTimeoutSeconds,
            Reason = reason
        };

        return SendMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends an error message.
    /// </summary>
    private async Task SendErrorAsync(
        string errorCode,
        string errorMessage,
        bool isFatal,
        CancellationToken cancellationToken)
    {
        var message = new ErrorMessage
        {
            ServerId = _serverId,
            Timestamp = _timeProvider.GetUtcNow(),
            ErrorCode = errorCode,
            Message = errorMessage,
            IsFatal = isFatal
        };

        await SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the pipe.
    /// </summary>
    private async Task<int> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await _pipeStream.ReadAsync(
                buffer[totalRead..],
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                // Connection closed
                return totalRead;
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Called when the connection is disconnected.
    /// </summary>
    private void OnDisconnected()
    {
        try
        {
            Disconnected?.Invoke(this, new PipeDisconnectedEventArgs
            {
                ServerId = _serverId,
                DisconnectedAt = _timeProvider.GetUtcNow()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in disconnect handler for server {ServerId}", _serverId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _connectionCts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancellation errors
        }

        try
        {
            _writeLock.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        try
        {
            await _pipeStream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore disposal errors
        }

        try
        {
            _connectionCts.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }
}

/// <summary>
/// Event args for received pipe messages.
/// </summary>
public sealed class PipeMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// The server ID that sent the message.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// The received message.
    /// </summary>
    public required PipeMessage Message { get; init; }

    /// <summary>
    /// When the message was received.
    /// </summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>
/// Event args for pipe disconnection.
/// </summary>
public sealed class PipeDisconnectedEventArgs : EventArgs
{
    /// <summary>
    /// The server ID that disconnected.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// When the disconnection occurred.
    /// </summary>
    public required DateTimeOffset DisconnectedAt { get; init; }
}
