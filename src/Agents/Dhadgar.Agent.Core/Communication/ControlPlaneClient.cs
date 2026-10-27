using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Telemetry;
using Dhadgar.Contracts.Nodes;
using Dhadgar.Shared.Results;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Communication;

/// <summary>
/// SignalR client for communication with the control plane.
/// </summary>
public sealed class ControlPlaneClient : IControlPlaneClient, IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly ICertificateStore _certificateStore;
    private readonly AgentMeter _meter;
    private readonly AgentActivitySource _activitySource;
    private readonly ILogger<ControlPlaneClient> _logger;
    private readonly ControlPlaneHubTransport _transport;

    private HubConnection? _hubConnection;
    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private X509Certificate2? _clientCertificate;

    public ConnectionState State => _state;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public event EventHandler<CommandReceivedEventArgs>? CommandReceived;

    public ControlPlaneClient(
        IOptions<AgentOptions> options,
        ICertificateStore certificateStore,
        AgentMeter meter,
        AgentActivitySource activitySource,
        ILogger<ControlPlaneClient> logger,
        ControlPlaneHubTransport? transport = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transport = transport ?? new ControlPlaneHubTransport();
    }

    public async Task<Result> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
            {
                return Result.Success();
            }

            SetState(ConnectionState.Connecting);

            if (!Uri.TryCreate(_options.ControlPlane.Endpoint, UriKind.Absolute, out var baseUri))
            {
                return Refuse("[ControlPlane.InvalidEndpoint] Control plane endpoint is not an absolute URI.");
            }

            // Security: Require HTTPS for control plane communication
            if (baseUri.Scheme != Uri.UriSchemeHttps)
            {
                return Refuse(
                    $"[ControlPlane.InsecureEndpoint] Control plane endpoint must use HTTPS. Current scheme: {baseUri.Scheme}");
            }

            var endpoint = new Uri(baseUri, AgentHubProtocol.Path);
            _logger.LogInformation("Connecting to control plane at {Endpoint}", endpoint);

            // Dispose existing connection and certificate before creating new one
            if (_hubConnection is not null)
            {
                _hubConnection.Closed -= OnConnectionClosed;
                _hubConnection.Reconnecting -= OnReconnecting;
                _hubConnection.Reconnected -= OnReconnected;
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }

            // Dispose previous certificate to prevent handle leaks
            _clientCertificate?.Dispose();
            _clientCertificate = null;

            // Get client certificate and track it for disposal
            _clientCertificate = _certificateStore.GetClientCertificate();

            // Security: Require a client certificate for every hub connection (mTLS). The node is identified
            // by the certificate on the Nodes side, so no node ID header is sent (D-S23).
            if (_clientCertificate is not { } clientCert)
            {
                return Refuse("[ControlPlane.NoCertificate] No client certificate is available. Enroll the agent first.");
            }

            var builder = new HubConnectionBuilder()
                .WithUrl(endpoint, options =>
                {
                    options.Transports = _transport.Transports;
                    if (_transport.HttpMessageHandlerFactory is { } handlerFactory)
                    {
                        options.HttpMessageHandlerFactory = handlerFactory;
                    }

                    options.ClientCertificates = [clientCert];
                    options.Headers[AgentHubProtocol.VersionHeader] = AgentHubProtocol.CurrentVersion;
                })
                .WithAutomaticReconnect(new RetryPolicy(_options.ControlPlane))
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                });

            _hubConnection = builder.Build();

            // Configure keep-alive and server timeout from options
            _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(_options.ControlPlane.KeepAliveIntervalSeconds);
            _hubConnection.ServerTimeout = TimeSpan.FromSeconds(_options.ControlPlane.ServerTimeoutSeconds);

            // Set up event handlers
            _hubConnection.Closed += OnConnectionClosed;
            _hubConnection.Reconnecting += OnReconnecting;
            _hubConnection.Reconnected += OnReconnected;

            // Register server-to-client methods
            _hubConnection.On<string>(nameof(IAgentHubClient.ReceiveCommand), OnCommandReceived);
            _hubConnection.On<string>(nameof(IAgentHubClient.ReceivePing), OnPingReceived);

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.ConnectionTimeoutSeconds));

            try
            {
                await _hubConnection.StartAsync(cts.Token);
            }
            catch (HttpRequestException ex)
            {
                var status = ex.StatusCode is { } code ? $" (HTTP {(int)code})" : string.Empty;
                return ConnectFailed($"transport error{status}", ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return ConnectFailed("timed out", ex);
            }
            catch (WebSocketException ex)
            {
                return ConnectFailed("WebSocket error", ex);
            }
            catch (InvalidOperationException ex)
            {
                return ConnectFailed("hub start failed", ex);
            }

            SetState(ConnectionState.Connected);
            _logger.LogInformation("Connected to control plane");
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation is the one exception ConnectAsync lets through (D-S227).
            SetState(ConnectionState.Failed, "Connect cancelled");
            throw;
        }
        catch (Exception ex)
        {
            // Any other fault (certificate store, hub builder) is a failed connect, never a throw (D-S24, F8, D-S227).
            _logger.LogError(ex, "Unexpected error while connecting to control plane");
            SetState(ConnectionState.Failed, ex.GetType().Name);
            return Result.Failure($"[ControlPlane.ConnectFailed] unexpected {ex.GetType().Name}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Refuses a connection attempt before any hub connection exists. Sets state to Failed.
    /// </summary>
    private Result Refuse(string error)
    {
        _logger.LogWarning("Control plane connection refused: {Error}", error);
        SetState(ConnectionState.Failed, error);
        return Result.Failure(error);
    }

    /// <summary>
    /// Maps a failed hub start to a Failure result. Sets state to Failed.
    /// </summary>
    private Result ConnectFailed(string detail, Exception ex)
    {
        _logger.LogWarning(ex, "Failed to connect to control plane: {Detail}", detail);
        SetState(ConnectionState.Failed, detail);
        return Result.Failure($"[ControlPlane.ConnectFailed] {detail}");
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_hubConnection is null || _state == ConnectionState.Disconnected)
            {
                return;
            }

            _logger.LogInformation("Disconnecting from control plane");

            await _hubConnection.StopAsync(cancellationToken);
            await _hubConnection.DisposeAsync();
            _hubConnection = null;

            SetState(ConnectionState.Disconnected);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<Result<AgentHeartbeatAck>> SendHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return Result<AgentHeartbeatAck>.Failure("[ControlPlane.NotConnected] Cannot send heartbeat: not connected");
        }

        using var activity = _activitySource.StartHeartbeat(_options.NodeId ?? Guid.Empty);

        try
        {
            // SECURITY: Use explicit invocation timeout to prevent hanging on slow/unresponsive server
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.InvocationTimeoutSeconds));
            var ack = await _hubConnection.InvokeAsync<AgentHeartbeatAck>(
                nameof(IAgentHub.Heartbeat), heartbeat, cts.Token);
            _meter.RecordHeartbeatSent();
            _logger.LogDebug("Heartbeat sent for node {NodeId}", _options.NodeId);
            return Result<AgentHeartbeatAck>.Success(ack);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Invocation timeout");
            _logger.LogWarning("Heartbeat invocation timed out after {Timeout}s",
                _options.ControlPlane.InvocationTimeoutSeconds);
            return Result<AgentHeartbeatAck>.Failure(
                $"[ControlPlane.Timeout] Heartbeat invocation timed out after {_options.ControlPlane.InvocationTimeoutSeconds}s");
        }
        catch (HubException ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Control plane hub rejected the heartbeat");
            return Result<AgentHeartbeatAck>.Failure("[ControlPlane.HubError] Control plane hub returned an error for the heartbeat");
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Heartbeat invocation failed: connection not active");
            return Result<AgentHeartbeatAck>.Failure("[ControlPlane.NotConnected] Connection dropped during heartbeat");
        }
    }

    public async Task<bool> SendCommandResultAsync(CommandResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot send command result: not connected");
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(result);
            // SECURITY: Use explicit invocation timeout to prevent hanging on slow/unresponsive server
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.InvocationTimeoutSeconds));
            await _hubConnection.InvokeAsync("CommandResult", json, cts.Token);
            _logger.LogDebug("Command result sent for command {CommandId}", result.CommandId);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Command result invocation timed out for {CommandId} after {Timeout}s",
                result.CommandId, _options.ControlPlane.InvocationTimeoutSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command result for {CommandId}", result.CommandId);
            return false;
        }
    }

    public async Task SendTelemetryAsync(TelemetryPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return; // Telemetry is best-effort, don't warn
        }

        try
        {
            var json = JsonSerializer.Serialize(payload);
            // SECURITY: Use explicit invocation timeout to prevent hanging on slow/unresponsive server
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.InvocationTimeoutSeconds));
            await _hubConnection.InvokeAsync("Telemetry", json, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send telemetry");
            // Telemetry failures are not critical (includes timeouts)
        }
    }

    /// <summary>
    /// Maximum allowed payload size for incoming commands (256 KB).
    /// </summary>
    private const int MaxCommandPayloadSize = 256 * 1024;

    /// <summary>
    /// Maximum length for command type in metrics to prevent cardinality explosion.
    /// </summary>
    private const int MaxCommandTypeLength = 64;

    /// <summary>
    /// Allowlist of known command types for metrics.
    /// Unknown types are recorded as "Unknown" to prevent metric cardinality explosion.
    /// </summary>
    private static readonly HashSet<string> KnownCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ping",
        "StartServer",
        "StopServer",
        "RestartServer",
        "UpdateServer",
        "InstallMod",
        "UninstallMod",
        "UpdateMod",
        "FileDownload",
        "FileUpload",
        "ExecuteCommand",
        "GetStatus",
        "GetLogs",
        "ConfigureServer",
        "Backup",
        "Restore"
    };

    /// <summary>
    /// JSON serializer options with security constraints.
    /// </summary>
    private static readonly JsonSerializerOptions CommandJsonOptions = new()
    {
        MaxDepth = 64,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Sanitizes a command type for use in metrics to prevent cardinality explosion.
    /// Returns "Unknown" for null, empty, too-long, or unrecognized command types.
    /// </summary>
    private static string SanitizeCommandTypeForMetrics(string? commandType)
    {
        if (string.IsNullOrEmpty(commandType) || commandType.Length > MaxCommandTypeLength)
        {
            return "Unknown";
        }

        return KnownCommandTypes.Contains(commandType) ? commandType : "Unknown";
    }

    private void OnCommandReceived(string commandJson)
    {
        try
        {
            // Security: Enforce payload size limit to prevent resource exhaustion
            // Use UTF-8 byte count, not character count, since multi-byte characters inflate actual size
            var payloadByteCount = System.Text.Encoding.UTF8.GetByteCount(commandJson);
            if (payloadByteCount > MaxCommandPayloadSize)
            {
                _logger.LogWarning(
                    "Rejected command: payload size {Size} bytes exceeds maximum {MaxSize}",
                    payloadByteCount, MaxCommandPayloadSize);
                return;
            }

            var envelope = JsonSerializer.Deserialize<CommandEnvelope>(commandJson, CommandJsonOptions);
            if (envelope is null)
            {
                _logger.LogWarning("Rejected command: deserialization returned null");
                return;
            }

            // Security: Validate NodeId matches this agent's NodeId
            if (_options.NodeId.HasValue && envelope.NodeId != _options.NodeId.Value)
            {
                _logger.LogWarning(
                    "Rejected command {CommandId}: NodeId mismatch (expected {ExpectedNodeId}, received {ReceivedNodeId})",
                    envelope.CommandId, _options.NodeId.Value, envelope.NodeId);
                return;
            }

            // Security: Validate OrganizationId matches this agent's OrganizationId (multi-tenant isolation)
            // SECURITY: Reject Guid.Empty as invalid - prevents bypass via empty OrganizationId
            if (_options.OrganizationId.HasValue &&
                (envelope.OrganizationId == Guid.Empty || envelope.OrganizationId != _options.OrganizationId.Value))
            {
                _logger.LogWarning(
                    "Rejected command {CommandId}: OrganizationId invalid or mismatch (expected {ExpectedOrgId}, received {ReceivedOrgId})",
                    envelope.CommandId, _options.OrganizationId.Value,
                    envelope.OrganizationId == Guid.Empty ? "(empty)" : envelope.OrganizationId.ToString());
                return;
            }

            // Security: Reject expired commands
            if (envelope.ExpiresAt.HasValue && envelope.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                _logger.LogWarning(
                    "Rejected command {CommandId}: expired at {ExpiresAt}",
                    envelope.CommandId, envelope.ExpiresAt.Value);
                return;
            }

            // SECURITY: Sanitize CommandType for metrics to prevent cardinality explosion
            // Untrusted input could flood metrics with unique values, causing memory exhaustion
            var metricCommandType = SanitizeCommandTypeForMetrics(envelope.CommandType);
            _meter.RecordCommandReceived(metricCommandType);
            _logger.LogInformation("Received command {CommandType} with ID {CommandId}",
                envelope.CommandType, envelope.CommandId);
            CommandReceived?.Invoke(this, new CommandReceivedEventArgs(envelope));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize command");
        }
    }

    private void OnPingReceived(string _)
    {
        _logger.LogDebug("Received ping from control plane");
    }

    private Task OnConnectionClosed(Exception? ex)
    {
        if (ex is not null)
        {
            _logger.LogWarning(ex, "Connection to control plane closed with error");
            SetState(ConnectionState.Disconnected, ex.Message);
        }
        else
        {
            _logger.LogInformation("Connection to control plane closed");
            SetState(ConnectionState.Disconnected);
        }
        return Task.CompletedTask;
    }

    private Task OnReconnecting(Exception? ex)
    {
        _meter.RecordReconnectAttempt();
        _logger.LogWarning(ex, "Reconnecting to control plane");
        SetState(ConnectionState.Reconnecting, ex?.Message);
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation("Reconnected to control plane. Connection ID: {ConnectionId}", connectionId);
        SetState(ConnectionState.Connected);
        return Task.CompletedTask;
    }

    private void SetState(ConnectionState newState, string? error = null)
    {
        var previousState = _state;
        _state = newState;

        using var activity = _activitySource.StartConnectionStateChange(
            previousState.ToString(), newState.ToString());

        if (error is not null)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, error);
        }

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previousState, newState, error));
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
        _clientCertificate?.Dispose();
        _connectionLock.Dispose();
    }

    /// <summary>
    /// Retry policy for SignalR reconnection with exponential backoff.
    /// </summary>
    private sealed class RetryPolicy : IRetryPolicy
    {
        private readonly ControlPlaneOptions _options;

        public RetryPolicy(ControlPlaneOptions options)
        {
            _options = options;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            // Exponential backoff with jitter
            var baseDelay = _options.ReconnectDelaySeconds;
            var maxDelay = _options.MaxReconnectDelaySeconds;

            var exponentialDelay = baseDelay * Math.Pow(2, retryContext.PreviousRetryCount);
            var cappedDelay = Math.Min(exponentialDelay, maxDelay);

            // Add jitter (up to 20% variation)
            // Using non-cryptographic random is appropriate for delay jitter
#pragma warning disable CA5394, SCS0005 // Non-cryptographic random is fine for jitter
            var jitter = Random.Shared.NextDouble() * 0.2;
#pragma warning restore CA5394, SCS0005
            var finalDelay = cappedDelay * (1 + jitter);

            return TimeSpan.FromSeconds(finalDelay);
        }
    }
}
