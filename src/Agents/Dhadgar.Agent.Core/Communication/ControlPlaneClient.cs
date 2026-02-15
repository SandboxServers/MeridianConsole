using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Dhadgar.Agent.Core.Authentication;
using Dhadgar.Agent.Core.Commands;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Health;
using Dhadgar.Agent.Core.Telemetry;
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
        ILogger<ControlPlaneClient> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _certificateStore = certificateStore ?? throw new ArgumentNullException(nameof(certificateStore));
        _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
            {
                return;
            }

            SetState(ConnectionState.Connecting);

            var baseUri = new Uri(_options.ControlPlane.Endpoint);

            // Security: Require HTTPS for control plane communication
            if (baseUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"Control plane endpoint must use HTTPS. Current scheme: {baseUri.Scheme}");
            }

            var endpoint = new Uri(baseUri, "/hubs/agent");
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Connecting to control plane at {Endpoint}", endpoint);
            }

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

            var builder = new HubConnectionBuilder()
                .WithUrl(endpoint, options =>
                {
                    // Configure mTLS if certificate is available
                    var clientCert = _clientCertificate;

                    // Security: Require client certificate when NodeId is set (enrolled agent)
                    if (_options.NodeId.HasValue)
                    {
                        if (clientCert is null)
                        {
                            throw new InvalidOperationException(
                                "Client certificate is required for enrolled agents (NodeId is set)");
                        }
                        options.ClientCertificates = [clientCert];
                        options.Headers["X-Node-Id"] = _options.NodeId.Value.ToString();
                    }
                    else if (clientCert is not null)
                    {
                        // Not enrolled yet, but have a cert (e.g., during enrollment)
                        options.ClientCertificates = [clientCert];
                    }
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
            _hubConnection.On<string>("ReceiveCommand", OnCommandReceived);
            _hubConnection.On<string>("ReceivePing", OnPingReceived);

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.ConnectionTimeoutSeconds));

            await _hubConnection.StartAsync(cts.Token);

            SetState(ConnectionState.Connected);
            _logger.LogInformation("Connected to control plane");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to control plane");
            SetState(ConnectionState.Failed, ex.Message);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
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

    public async Task SendHeartbeatAsync(HeartbeatPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot send heartbeat: not connected");
            return;
        }

        using var activity = _activitySource.StartHeartbeat(payload.NodeId);

        try
        {
            var json = JsonSerializer.Serialize(payload);
            // SECURITY: Use explicit invocation timeout to prevent hanging on slow/unresponsive server
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.ControlPlane.InvocationTimeoutSeconds));
            await _hubConnection.InvokeAsync("Heartbeat", json, cts.Token);
            _meter.RecordHeartbeatSent();
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Heartbeat sent for node {NodeId}", payload.NodeId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Invocation timeout");
            _logger.LogWarning("Heartbeat invocation timed out after {Timeout}s",
                _options.ControlPlane.InvocationTimeoutSeconds);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to send heartbeat");
            throw;
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
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Command result sent for command {CommandId}", result.CommandId);
            }
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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Received command {CommandType} with ID {CommandId}",
                    envelope.CommandType, envelope.CommandId);
            }
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
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Reconnected to control plane. Connection ID: {ConnectionId}", connectionId);
        }
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
