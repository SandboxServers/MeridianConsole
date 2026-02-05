using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

using Dhadgar.Agent.Core.Process;
using Dhadgar.Shared.Results;

using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.Windows.IPC;

/// <summary>
/// Interface for the Agent's named pipe server.
/// </summary>
public interface IAgentPipeServer : IAsyncDisposable
{
    /// <summary>
    /// Starts the pipe server and begins accepting connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the pipe server.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Event raised when output is received from a game server.
    /// </summary>
    event EventHandler<ServerOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Event raised when a game server's status changes.
    /// </summary>
    event EventHandler<ServerStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Sends a command to a specific game server.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="payload">Optional command payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> SendCommandAsync(
        string serverId,
        GameServerCommand command,
        string? payload = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends input to a game server's stdin.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="input">The input text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> SendInputAsync(
        string serverId,
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a game server for pipe communication.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    /// <param name="serviceAccountName">The service account name for access control (e.g., "NT SERVICE\MeridianGS_abc123").</param>
    /// <returns>Result indicating success or failure with error message.</returns>
    Result RegisterServer(string serverId, string serviceAccountName);

    /// <summary>
    /// Unregisters a game server.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    void UnregisterServer(string serverId);

    /// <summary>
    /// Checks if a server is connected via pipe.
    /// </summary>
    /// <param name="serverId">The server identifier.</param>
    bool IsServerConnected(string serverId);
}

/// <summary>
/// Named pipe server for IPC with game server wrappers.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This server handles IPC from game server processes.
///
/// Security measures:
/// - Each pipe is created with specific ACLs allowing only the designated service account
/// - Pipe names include the agent ID to prevent cross-agent hijacking
/// - Message size limits prevent memory exhaustion
/// - All data is validated before processing
/// </remarks>
public sealed partial class AgentPipeServer : IAgentPipeServer
{
    private readonly ILogger<AgentPipeServer> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Guid _agentId;
    private readonly ConcurrentDictionary<string, ServerRegistration> _registrations = new();
    private readonly ConcurrentDictionary<string, PipeClientConnection> _connections = new();
    private readonly CancellationTokenSource _serverCts = new();
    private readonly object _startLock = new();
    private volatile bool _started;
    private volatile bool _disposed;

    /// <summary>
    /// Pattern for valid server IDs.
    /// </summary>
    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled)]
    private static partial Regex ValidServerIdPattern();

    /// <summary>
    /// Event raised when output is received from a game server.
    /// </summary>
    public event EventHandler<ServerOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Event raised when a game server's status changes.
    /// </summary>
    public event EventHandler<ServerStatusEventArgs>? StatusChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentPipeServer"/> class.
    /// </summary>
    /// <param name="agentId">The agent's node ID.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Optional time provider for testability.</param>
    public AgentPipeServer(
        Guid agentId,
        ILogger<AgentPipeServer> logger,
        TimeProvider? timeProvider = null)
    {
        _agentId = agentId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_startLock)
        {
            if (_started)
            {
                _logger.LogDebug("Pipe server already started");
                return Task.CompletedTask;
            }

            _started = true;
        }

        _logger.LogInformation("Agent pipe server started for agent {AgentId}", _agentId);

        // Start listening for registered servers with exception handling
        foreach (var (serverId, _) in _registrations)
        {
            _ = StartListeningWithExceptionHandlingAsync(serverId, _serverCts.Token);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!_started || _disposed)
        {
            return;
        }

        _logger.LogInformation("Stopping agent pipe server");

        try
        {
            await _serverCts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancellation errors
        }

        // Close all connections
        foreach (var (serverId, connection) in _connections)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing connection for server {ServerId}", serverId);
            }
        }

        _connections.Clear();

        _logger.LogInformation("Agent pipe server stopped");
    }

    /// <inheritdoc />
    public Result RegisterServer(string serverId, string serviceAccountName)
    {
        if (_disposed)
        {
            return Result.Failure("[Pipe.Disposed] Agent pipe server has been disposed");
        }

        if (string.IsNullOrWhiteSpace(serverId))
        {
            return Result.Failure("[Pipe.InvalidServerId] Server ID is required");
        }

        if (!ValidServerIdPattern().IsMatch(serverId))
        {
            return Result.Failure("[Pipe.InvalidServerId] Invalid server ID format - must be alphanumeric with hyphens/underscores");
        }

        if (string.IsNullOrWhiteSpace(serviceAccountName))
        {
            return Result.Failure("[Pipe.InvalidServiceAccount] Service account name is required");
        }

        var pipeName = GetPipeName(serverId);
        var registration = new ServerRegistration(serverId, serviceAccountName, pipeName);

        _registrations[serverId] = registration;

        _logger.LogInformation(
            "Registered server {ServerId} for pipe communication on {PipeName}",
            serverId, pipeName);

        // If already started, begin listening for this server with exception handling
        if (_started)
        {
            _ = StartListeningWithExceptionHandlingAsync(serverId, _serverCts.Token);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public void UnregisterServer(string serverId)
    {
        _registrations.TryRemove(serverId, out _);

        // Dispose pattern: The connection is either transferred to DisposeConnectionAsync (fire-and-forget)
        // or disposed in the finally block. The analyzer doesn't understand the ownership transfer pattern,
        // so we suppress CA2000 for the TryRemove call while ensuring disposal in finally.
        PipeClientConnection? connection = null;
        try
        {
#pragma warning disable CA2000 // Dispose objects before losing scope - ownership transferred to DisposeConnectionAsync or disposed in finally
            if (_connections.TryRemove(serverId, out var removed))
            {
                connection = removed;
            }
#pragma warning restore CA2000

            if (connection is not null)
            {
                // Fire and forget disposal - transfers ownership to the async task
                _ = DisposeConnectionAsync(connection);
                // Null out to indicate ownership was transferred (prevents double dispose in finally)
                connection = null;
            }
        }
        finally
        {
            // Dispose if ownership wasn't successfully transferred to DisposeConnectionAsync
            if (connection is not null)
            {
                try
                {
                    // PipeClientConnection implements IAsyncDisposable, so we must use DisposeAsync
                    connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception disposeEx)
                {
                    _logger.LogError(disposeEx, "Failed to dispose connection for server {ServerId}", serverId);
                }
            }
        }

        _logger.LogInformation("Unregistered server {ServerId}", serverId);
    }

    /// <inheritdoc />
    public bool IsServerConnected(string serverId)
    {
        return _connections.TryGetValue(serverId, out var connection) && connection.IsConnected;
    }

    /// <inheritdoc />
    public async Task<Result> SendCommandAsync(
        string serverId,
        GameServerCommand command,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            return Result.Failure($"[Pipe.NotConnected] Server {serverId} is not connected");
        }

        var sent = await connection.SendCommandAsync(command, payload, 30, cancellationToken)
            .ConfigureAwait(false);

        if (!sent)
        {
            return Result.Failure($"[Pipe.SendFailed] Failed to send command to server {serverId}");
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SendInputAsync(
        string serverId,
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            return Result.Failure($"[Pipe.NotConnected] Server {serverId} is not connected");
        }

        var sent = await connection.SendInputAsync(input, cancellationToken).ConfigureAwait(false);

        if (!sent)
        {
            return Result.Failure($"[Pipe.SendFailed] Failed to send input to server {serverId}");
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync().ConfigureAwait(false);

        try
        {
            _serverCts.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }

    #region Private Methods

    /// <summary>
    /// Starts listening for a server with exception handling for fire-and-forget scenarios.
    /// </summary>
    private async Task StartListeningWithExceptionHandlingAsync(string serverId, CancellationToken cancellationToken)
    {
        try
        {
            await StartListeningForServerAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in listener for server {ServerId}", serverId);
        }
    }

    /// <summary>
    /// Disposes a connection asynchronously (helper to avoid CA2012).
    /// </summary>
    private static async Task DisposeConnectionAsync(PipeClientConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore disposal errors
        }
    }

    /// <summary>
    /// Gets the pipe name for a server.
    /// Format: MeridianAgent_{agentId}\{serverId}
    /// </summary>
    private string GetPipeName(string serverId)
    {
        return $@"MeridianAgent_{_agentId:N}\{serverId}";
    }

    /// <summary>
    /// Starts listening for connections from a specific server.
    /// </summary>
    /// <remarks>
    /// CA2000 is suppressed because ownership of pipeStream is transferred to PipeClientConnection
    /// on successful connection, or disposed in the finally block on all failure paths.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership transferred to PipeClientConnection on success, cleaned up in finally on failure")]
    private async Task StartListeningForServerAsync(string serverId, CancellationToken cancellationToken)
    {
        if (!_registrations.TryGetValue(serverId, out var registration))
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested && _registrations.ContainsKey(serverId))
        {
            NamedPipeServerStream? pipeStream = null;

            try
            {
                // Create pipe with security
                var pipeSecurity = CreatePipeSecurity(registration.ServiceAccountName);

                pipeStream = NamedPipeServerStreamAcl.Create(
                    registration.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1, // One connection per server
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                    inBufferSize: PipeMessage.MaxMessageSize,
                    outBufferSize: PipeMessage.MaxMessageSize,
                    pipeSecurity);

                _logger.LogDebug(
                    "Waiting for connection from server {ServerId} on pipe {PipeName}",
                    serverId, registration.PipeName);

                await pipeStream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Server {ServerId} connected via pipe {PipeName}",
                    serverId, registration.PipeName);

                // Create and track the connection
                var connection = new PipeClientConnection(
                    pipeStream,
                    serverId,
                    _logger,
                    _timeProvider);

                // Wire up events
                connection.MessageReceived += OnMessageReceived;
                connection.Disconnected += OnConnectionDisconnected;

                // Store connection (replaces any existing)
                if (_connections.TryRemove(serverId, out var oldConnection))
                {
                    // Unwire events before disposing to prevent handler races
                    oldConnection.MessageReceived -= OnMessageReceived;
                    oldConnection.Disconnected -= OnConnectionDisconnected;
                    await oldConnection.DisposeAsync().ConfigureAwait(false);
                }

                _connections[serverId] = connection;

                // Transfer ownership - don't dispose pipeStream
                pipeStream = null;

                // Start reading in background
                _ = connection.StartReadingAsync(cancellationToken);

                // Wait for disconnection before accepting new connection
                // (only one wrapper per server can connect at a time)
                while (connection.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "IO error on pipe for server {ServerId}, will retry", serverId);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error accepting pipe connection for server {ServerId}", serverId);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (pipeStream is not null)
                {
                    await pipeStream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug("Stopped listening for server {ServerId}", serverId);
    }

    /// <summary>
    /// Creates pipe security settings.
    /// </summary>
    private static PipeSecurity CreatePipeSecurity(string serviceAccountName)
    {
        var security = new PipeSecurity();

        // Grant the agent (running as SYSTEM or dedicated account) full access
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Grant the specific game server's service account read/write access
        // SECURITY: NTAccount constructor accepts the name but does NOT validate existence.
        // However, NTAccount.Translate() or ACL application will throw IdentityNotMappedException
        // if the account doesn't exist. For Virtual Service Accounts, the account is created
        // when the service first starts, so this may throw if called before service creation.
        var serviceAccount = new NTAccount(serviceAccountName);
        security.AddAccessRule(new PipeAccessRule(
            serviceAccount,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        // Grant BUILTIN\Administrators read/write for debugging
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(
            adminsSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }

    /// <summary>
    /// Handles messages received from game server wrappers.
    /// </summary>
    private void OnMessageReceived(object? sender, PipeMessageReceivedEventArgs e)
    {
        try
        {
            switch (e.Message)
            {
                case OutputMessage output:
                    HandleOutputMessage(e.ServerId, output);
                    break;

                case StatusMessage status:
                    HandleStatusMessage(e.ServerId, status);
                    break;

                case HeartbeatMessage heartbeat:
                    HandleHeartbeatMessage(e.ServerId, heartbeat);
                    break;

                case AcknowledgeMessage ack:
                    HandleAcknowledgeMessage(e.ServerId, ack);
                    break;

                case ErrorMessage error:
                    HandleErrorMessage(e.ServerId, error);
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown message type from server {ServerId}: {Type}",
                        e.ServerId, e.Message.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from server {ServerId}", e.ServerId);
        }
    }

    private void HandleOutputMessage(string serverId, OutputMessage output)
    {
        // Truncate if needed (defense in depth)
        var data = output.Data;
        if (data.Length > PipeMessage.MaxOutputLineLength)
        {
            data = data[..PipeMessage.MaxOutputLineLength] + "... [TRUNCATED]";
        }

        try
        {
            OutputReceived?.Invoke(this, new ServerOutputEventArgs
            {
                ServerId = serverId,
                Data = data,
                IsError = output.IsError,
                Timestamp = output.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in output handler for server {ServerId}", serverId);
        }
    }

    private void HandleStatusMessage(string serverId, StatusMessage status)
    {
        _logger.LogDebug(
            "Status update from server {ServerId}: {State} (PID: {Pid})",
            serverId, status.State, status.OsPid);

        try
        {
            StatusChanged?.Invoke(this, new ServerStatusEventArgs
            {
                ServerId = serverId,
                State = status.State,
                OsPid = status.OsPid,
                ExitCode = status.ExitCode,
                Message = status.Message,
                CpuPercent = status.CpuPercent,
                MemoryBytes = status.MemoryBytes,
                Timestamp = status.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in status handler for server {ServerId}", serverId);
        }
    }

    private void HandleHeartbeatMessage(string serverId, HeartbeatMessage heartbeat)
    {
        _logger.LogTrace(
            "Heartbeat from server {ServerId}, sequence {Sequence}",
            serverId, heartbeat.Sequence);
    }

    private void HandleAcknowledgeMessage(string serverId, AcknowledgeMessage ack)
    {
        if (!ack.Success)
        {
            _logger.LogWarning(
                "Command {CorrelationId} failed for server {ServerId}: {Error}",
                ack.AcknowledgedId, serverId, ack.ErrorMessage);
        }
        else
        {
            _logger.LogDebug(
                "Command {CorrelationId} acknowledged by server {ServerId}",
                ack.AcknowledgedId, serverId);
        }
    }

    private void HandleErrorMessage(string serverId, ErrorMessage error)
    {
        if (error.IsFatal)
        {
            _logger.LogError(
                "Fatal error from server {ServerId}: [{Code}] {Message}",
                serverId, error.ErrorCode, error.Message);
        }
        else
        {
            _logger.LogWarning(
                "Error from server {ServerId}: [{Code}] {Message}",
                serverId, error.ErrorCode, error.Message);
        }
    }

    /// <summary>
    /// Handles connection disconnection.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership transferred to DisposeConnectionAsync")]
    private void OnConnectionDisconnected(object? sender, PipeDisconnectedEventArgs e)
    {
        _logger.LogInformation(
            "Server {ServerId} disconnected from pipe",
            e.ServerId);

        if (_connections.TryRemove(e.ServerId, out var connection))
        {
            // Dispose the connection to release pipe handle and semaphore
            _ = DisposeConnectionAsync(connection);
        }

        // Notify status changed to stopped
        try
        {
            StatusChanged?.Invoke(this, new ServerStatusEventArgs
            {
                ServerId = e.ServerId,
                State = GameServerState.Stopped,
                Timestamp = e.DisconnectedAt,
                Message = "Pipe connection lost"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in disconnect handler for server {ServerId}", e.ServerId);
        }
    }

    #endregion

    #region Private Types

    /// <summary>
    /// Registration information for a server.
    /// </summary>
    private sealed record ServerRegistration(
        string ServerId,
        string ServiceAccountName,
        string PipeName);

    #endregion
}

/// <summary>
/// Event args for server output.
/// </summary>
public sealed class ServerOutputEventArgs : EventArgs
{
    /// <summary>
    /// The server identifier.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// The output data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// True if this is stderr output.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>
    /// When the output was generated.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Event args for server status changes.
/// </summary>
public sealed class ServerStatusEventArgs : EventArgs
{
    /// <summary>
    /// The server identifier.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// The new server state.
    /// </summary>
    public required GameServerState State { get; init; }

    /// <summary>
    /// OS process ID if running.
    /// </summary>
    public int? OsPid { get; init; }

    /// <summary>
    /// Exit code if stopped.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public double? CpuPercent { get; init; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public long? MemoryBytes { get; init; }

    /// <summary>
    /// When the status was reported.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
