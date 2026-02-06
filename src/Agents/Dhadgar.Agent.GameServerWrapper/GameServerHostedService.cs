using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dhadgar.Agent.GameServerWrapper;

/// <summary>
/// Hosted service that manages the game server process lifecycle.
/// </summary>
/// <remarks>
/// SECURITY CRITICAL: This service runs as a Windows Service with its own
/// Virtual Service Account, providing process isolation for the game server.
///
/// Responsibilities:
/// 1. Connect to agent via named pipe
/// 2. Launch and monitor the game server process
/// 3. Forward stdout/stderr to agent
/// 4. Accept commands from agent
/// 5. Handle graceful shutdown
/// </remarks>
public sealed class GameServerHostedService : BackgroundService, IAsyncDisposable
{
    private readonly WrapperOptions _options;
    private readonly ILogger<GameServerHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeProvider _timeProvider;

    private NamedPipeClientStream? _pipeClient;
    private Process? _gameServerProcess;
    private GameServerState _currentState = GameServerState.Initializing;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Maximum output line length (64KB).
    /// </summary>
    private const int MaxOutputLineLength = 64 * 1024;

    /// <summary>
    /// Maximum message size (256KB).
    /// </summary>
    private const int MaxMessageSize = 256 * 1024;

    /// <summary>
    /// Timeout for pipe connection.
    /// </summary>
    private static readonly TimeSpan PipeConnectionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="GameServerHostedService"/> class.
    /// </summary>
    public GameServerHostedService(
        WrapperOptions options,
        ILogger<GameServerHostedService> logger,
        IHostApplicationLifetime lifetime,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GameServerWrapper starting for server {ServerId}",
            _options.ServerId);

        try
        {
            // Load server configuration
            var configResult = ServerConfig.LoadFromFile(_options.ConfigPath);
            if (!configResult.IsSuccess)
            {
                _logger.LogCritical("Failed to load configuration: {Error}", configResult.Error);
                _lifetime.StopApplication();
                return;
            }

            var config = configResult.Value;

            // Connect to agent pipe
            var connected = await ConnectToPipeAsync(stoppingToken).ConfigureAwait(false);
            if (!connected)
            {
                _logger.LogCritical("Failed to connect to agent pipe");
                _lifetime.StopApplication();
                return;
            }

            // Start reading from pipe in background
            var pipeReadTask = ReadFromPipeAsync(stoppingToken);

            // Start the game server
            await StartGameServerAsync(config, stoppingToken).ConfigureAwait(false);

            // Wait for cancellation (service stop)
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }

            // Stop the game server gracefully
            await StopGameServerAsync(TimeSpan.FromSeconds(config.GracefulShutdownTimeoutSeconds))
                .ConfigureAwait(false);

            // Wait for pipe read to complete
            try
            {
                await pipeReadTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in GameServerWrapper");
            _lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Connects to the agent's named pipe.
    /// </summary>
    private async Task<bool> ConnectToPipeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connecting to agent pipe: {PipeName}", _options.PipeName);

        try
        {
            _pipeClient = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _options.PipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous | PipeOptions.WriteThrough);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PipeConnectionTimeout);

            await _pipeClient.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            _logger.LogInformation("Connected to agent pipe");

            // Send initial status
            await SendStatusAsync(GameServerState.Initializing).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Timeout connecting to agent pipe");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to agent pipe");
            return false;
        }
    }

    /// <summary>
    /// Reads messages from the agent pipe.
    /// </summary>
    private async Task ReadFromPipeAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient is null || !_pipeClient.IsConnected)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaxMessageSize + 4);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipeClient.IsConnected)
            {
                try
                {
                    // Read length prefix
                    var lengthRead = await ReadExactAsync(_pipeClient, buffer.AsMemory(0, 4), cancellationToken)
                        .ConfigureAwait(false);

                    if (lengthRead == 0)
                    {
                        _logger.LogInformation("Agent pipe closed");
                        break;
                    }

                    if (lengthRead < 4)
                    {
                        _logger.LogWarning("Incomplete length prefix");
                        break;
                    }

                    var messageLength = BitConverter.ToInt32(buffer, 0);

                    if (messageLength <= 0 || messageLength > MaxMessageSize)
                    {
                        _logger.LogWarning(
                            "Invalid message length: {Length}. Terminating read loop to prevent frame desynchronization.",
                            messageLength);
                        break;
                    }

                    // Read message body
                    var messageRead = await ReadExactAsync(
                        _pipeClient,
                        buffer.AsMemory(0, messageLength),
                        cancellationToken).ConfigureAwait(false);

                    if (messageRead < messageLength)
                    {
                        _logger.LogWarning("Incomplete message");
                        break;
                    }

                    // Parse and handle message
                    await HandleMessageAsync(buffer.AsMemory(0, messageLength), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (IOException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("IO error reading from pipe");
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Handles a message from the agent.
    /// </summary>
    private async Task HandleMessageAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<JsonElement>(data.Span);

            if (!message.TryGetProperty("type", out var typeProperty))
            {
                _logger.LogWarning("Message missing type property");
                return;
            }

            var type = typeProperty.GetString();

            switch (type)
            {
                case "command":
                    await HandleCommandAsync(message, cancellationToken).ConfigureAwait(false);
                    break;

                case "input":
                    await HandleInputAsync(message).ConfigureAwait(false);
                    break;

                case "heartbeat":
                    await HandleHeartbeatAsync(message).ConfigureAwait(false);
                    break;

                case "shutdown":
                    await HandleShutdownAsync(message).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {Type}", type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse message");
        }
    }

    /// <summary>
    /// Handles a command message.
    /// </summary>
    private async Task HandleCommandAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("command", out var commandProperty))
        {
            return;
        }

        var command = commandProperty.GetString();
        var correlationId = message.TryGetProperty("correlationId", out var corrProp)
            ? corrProp.GetString()
            : null;

        _logger.LogDebug("Received command: {Command}", command);

        var success = true;
        string? errorMessage = null;

        try
        {
            switch (command)
            {
                case "GetStatus":
                    await SendStatusAsync(_currentState).ConfigureAwait(false);
                    break;

                case "Stop":
                    var timeout = message.TryGetProperty("timeoutSeconds", out var timeoutProp)
                        ? timeoutProp.GetInt32()
                        : 30;
                    await StopGameServerAsync(TimeSpan.FromSeconds(timeout)).ConfigureAwait(false);
                    break;

                case "Kill":
                    KillGameServer();
                    break;

                default:
                    _logger.LogWarning("Unknown command: {Command}", command);
                    success = false;
                    errorMessage = $"Unknown command: {command}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Command}", command);
            success = false;
            errorMessage = ex.Message;
        }

        if (correlationId is not null)
        {
            await SendAckAsync(correlationId, success, errorMessage).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles an input message (stdin for game server).
    /// </summary>
    private async Task HandleInputAsync(JsonElement message)
    {
        if (!message.TryGetProperty("input", out var inputProperty))
        {
            return;
        }

        var input = inputProperty.GetString();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        if (_gameServerProcess is null || _gameServerProcess.HasExited)
        {
            _logger.LogWarning("Cannot send input - game server not running");
            return;
        }

        // Check if stdin was redirected before attempting to write
        if (!_gameServerProcess.StartInfo.RedirectStandardInput)
        {
            _logger.LogWarning("Cannot send input - stdin was not redirected for this game server");
            return;
        }

        try
        {
            await _gameServerProcess.StandardInput.WriteLineAsync(input).ConfigureAwait(false);
            await _gameServerProcess.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send input to game server");
        }
    }

    /// <summary>
    /// Handles a heartbeat message.
    /// </summary>
    private async Task HandleHeartbeatAsync(JsonElement message)
    {
        var sequence = message.TryGetProperty("sequence", out var seqProp)
            ? seqProp.GetInt64()
            : 0;

        _logger.LogTrace("Heartbeat received, sequence {Sequence}", sequence);

        // Send heartbeat response
        await SendMessageAsync(new
        {
            type = "heartbeat",
            serverId = _options.ServerId,
            sequence,
            timestamp = _timeProvider.GetUtcNow()
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a shutdown message.
    /// </summary>
    private async Task HandleShutdownAsync(JsonElement message)
    {
        var timeout = message.TryGetProperty("gracefulTimeoutSeconds", out var timeoutProp)
            ? timeoutProp.GetInt32()
            : 30;

        var reason = message.TryGetProperty("reason", out var reasonProp)
            ? reasonProp.GetString()
            : null;

        _logger.LogInformation("Shutdown requested: {Reason}", reason ?? "no reason provided");

        await StopGameServerAsync(TimeSpan.FromSeconds(timeout)).ConfigureAwait(false);
        _lifetime.StopApplication();
    }

    /// <summary>
    /// Starts the game server process.
    /// </summary>
    private async Task StartGameServerAsync(ServerConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting game server: {Executable}", config.ExecutablePath);

        _currentState = GameServerState.Starting;
        await SendStatusAsync(_currentState).ConfigureAwait(false);

        try
        {
            // Compute working directory - Path.GetDirectoryName can return null for root paths
            var workingDir = config.WorkingDirectory
                ?? Path.GetDirectoryName(config.ExecutablePath)
                ?? Environment.CurrentDirectory;

            var startInfo = new ProcessStartInfo
            {
                FileName = config.ExecutablePath,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = config.CaptureStdout,
                RedirectStandardError = config.CaptureStderr,
                RedirectStandardInput = config.RedirectStdin
            };

            // Add arguments
            if (!string.IsNullOrWhiteSpace(config.Arguments))
            {
                // Use ArgumentList for safety
                foreach (var arg in ParseArguments(config.Arguments))
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            // Set environment variables
            if (config.EnvironmentVariables is not null)
            {
                foreach (var (key, value) in config.EnvironmentVariables)
                {
                    startInfo.Environment[key] = value;
                }
            }

            _gameServerProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            // Wire up output handlers
            if (config.CaptureStdout)
            {
                _gameServerProcess.OutputDataReceived += (_, e) => OnOutputReceived(e.Data, isError: false);
            }

            if (config.CaptureStderr)
            {
                _gameServerProcess.ErrorDataReceived += (_, e) => OnOutputReceived(e.Data, isError: true);
            }

            // Wire up exit handler
            _gameServerProcess.Exited += OnProcessExited;

            // Start the process
            if (!_gameServerProcess.Start())
            {
                throw new InvalidOperationException("Failed to start game server process");
            }

            // Begin async reading
            if (config.CaptureStdout)
            {
                _gameServerProcess.BeginOutputReadLine();
            }

            if (config.CaptureStderr)
            {
                _gameServerProcess.BeginErrorReadLine();
            }

            _currentState = GameServerState.Running;
            await SendStatusAsync(_currentState, _gameServerProcess.Id).ConfigureAwait(false);

            _logger.LogInformation(
                "Game server started with PID {Pid}",
                _gameServerProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start game server");
            _currentState = GameServerState.Failed;
            await SendStatusAsync(_currentState, message: ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Stops the game server gracefully.
    /// </summary>
    private async Task StopGameServerAsync(TimeSpan timeout)
    {
        if (_gameServerProcess is null || _gameServerProcess.HasExited)
        {
            _currentState = GameServerState.Stopped;
            await SendStatusAsync(_currentState).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("Stopping game server with {Timeout} timeout", timeout);

        _currentState = GameServerState.Stopping;
        await SendStatusAsync(_currentState).ConfigureAwait(false);

        try
        {
            // Try graceful shutdown via CloseMainWindow
            try
            {
                if (_gameServerProcess.MainWindowHandle != IntPtr.Zero)
                {
                    _gameServerProcess.CloseMainWindow();
                }
            }
            catch
            {
                // May not have a window
            }

            // Wait for graceful exit
            using var timeoutCts = new CancellationTokenSource(timeout);

            try
            {
                await _gameServerProcess.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Graceful shutdown timeout, force killing");
                KillGameServer();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during game server shutdown");
            KillGameServer();
        }

        _currentState = GameServerState.Stopped;

        // Only access ExitCode when process has fully exited to avoid InvalidOperationException
        int? exitCode = null;
        if (_gameServerProcess is { HasExited: true })
        {
            exitCode = _gameServerProcess.ExitCode;
        }

        await SendStatusAsync(_currentState, exitCode: exitCode).ConfigureAwait(false);
    }

    /// <summary>
    /// Force kills the game server.
    /// </summary>
    private void KillGameServer()
    {
        if (_gameServerProcess is null || _gameServerProcess.HasExited)
        {
            return;
        }

        _logger.LogWarning("Force killing game server");

        try
        {
            _gameServerProcess.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error killing game server");
        }
    }

    /// <summary>
    /// Handles process exit event.
    /// </summary>
    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_gameServerProcess is null)
        {
            return;
        }

        var exitCode = _gameServerProcess.ExitCode;
        _logger.LogInformation("Game server exited with code {ExitCode}", exitCode);

        _currentState = exitCode == 0 ? GameServerState.Stopped : GameServerState.Failed;

        // Fire-and-forget with exception handling to prevent lost exceptions
        _ = SendStatusWithExceptionHandlingAsync(_currentState, exitCode: exitCode);
    }

    /// <summary>
    /// Sends status with exception handling for fire-and-forget scenarios.
    /// </summary>
    private async Task SendStatusWithExceptionHandlingAsync(
        GameServerState state,
        int? osPid = null,
        int? exitCode = null,
        string? message = null)
    {
        try
        {
            await SendStatusAsync(state, osPid, exitCode, message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send status update for state {State}", state);
        }
    }

    /// <summary>
    /// Handles output from the game server.
    /// </summary>
    private void OnOutputReceived(string? data, bool isError)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        // Truncate if needed
        if (data.Length > MaxOutputLineLength)
        {
            data = data[..MaxOutputLineLength] + "... [TRUNCATED]";
        }

        // Fire-and-forget with exception handling to prevent lost exceptions
        _ = SendOutputWithExceptionHandlingAsync(data, isError);
    }

    /// <summary>
    /// Sends output with exception handling for fire-and-forget scenarios.
    /// </summary>
    private async Task SendOutputWithExceptionHandlingAsync(string data, bool isError)
    {
        try
        {
            await SendOutputAsync(data, isError).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send output to agent");
        }
    }

    #region Pipe Communication

    private async Task SendStatusAsync(
        GameServerState state,
        int? osPid = null,
        int? exitCode = null,
        string? message = null)
    {
        await SendMessageAsync(new
        {
            type = "status",
            serverId = _options.ServerId,
            state = state.ToString(),
            osPid,
            exitCode,
            message,
            timestamp = _timeProvider.GetUtcNow()
        }).ConfigureAwait(false);
    }

    private async Task SendOutputAsync(string data, bool isError)
    {
        await SendMessageAsync(new
        {
            type = "output",
            serverId = _options.ServerId,
            data,
            isError,
            timestamp = _timeProvider.GetUtcNow()
        }).ConfigureAwait(false);
    }

    private async Task SendAckAsync(string correlationId, bool success, string? errorMessage = null)
    {
        await SendMessageAsync(new
        {
            type = "ack",
            serverId = _options.ServerId,
            acknowledgedId = correlationId,
            success,
            errorMessage,
            timestamp = _timeProvider.GetUtcNow()
        }).ConfigureAwait(false);
    }

    private async Task SendMessageAsync(object message)
    {
        if (_pipeClient is null || !_pipeClient.IsConnected)
        {
            return;
        }

        // Use a timeout to prevent holding the lock forever on a stalled pipe
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var lockTaken = false;

        try
        {
            lockTaken = await _writeLock.WaitAsync(TimeSpan.FromSeconds(5), timeoutCts.Token).ConfigureAwait(false);
            if (!lockTaken)
            {
                _logger.LogWarning("Timed out waiting for write lock in SendMessageAsync");
                return;
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(message);
            var lengthPrefix = BitConverter.GetBytes(json.Length);

            await _pipeClient.WriteAsync(lengthPrefix, timeoutCts.Token).ConfigureAwait(false);
            await _pipeClient.WriteAsync(json, timeoutCts.Token).ConfigureAwait(false);
            await _pipeClient.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SendMessageAsync timed out while writing to pipe");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error sending message to agent");
        }
        finally
        {
            if (lockTaken)
            {
                _writeLock.Release();
            }
        }
    }

    private static async Task<int> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer[totalRead..], cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
            {
                return totalRead;
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }

    #endregion

    #region Helpers

    private static List<string> ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new List<string>();
        }

        // Argument parsing that respects quotes and escape sequences (\\ and \")
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaped = false;

        foreach (var c in commandLine)
        {
            if (escaped)
            {
                // Handle escape sequences: \" becomes ", \\ becomes \
                if (c == '"' || c == '\\')
                {
                    current.Append(c);
                }
                else
                {
                    // Invalid escape sequence - keep the backslash and current char
                    current.Append('\\');
                    current.Append(c);
                }
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        // Handle trailing backslash at end of string
        if (escaped)
        {
            current.Append('\\');
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    #endregion

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();

        if (_pipeClient is not null)
        {
            await _pipeClient.DisposeAsync().ConfigureAwait(false);
        }

        // Unregister event handler before disposing to prevent ObjectDisposedException
        if (_gameServerProcess is not null)
        {
            _gameServerProcess.Exited -= OnProcessExited;
            _gameServerProcess.Dispose();
        }

        // Call base class cleanup (BackgroundService implements IDisposable)
        Dispose();
    }
}

/// <summary>
/// State of a game server process (duplicated for wrapper independence).
/// </summary>
public enum GameServerState
{
    Initializing,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed,
    Restarting
}
