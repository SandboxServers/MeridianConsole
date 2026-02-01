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

            var endpoint = new Uri(new Uri(_options.ControlPlane.Endpoint), "/hubs/agent");
            _logger.LogInformation("Connecting to control plane at {Endpoint}", endpoint);

            var builder = new HubConnectionBuilder()
                .WithUrl(endpoint, options =>
                {
                    // Configure mTLS if certificate is available
                    var clientCert = _certificateStore.GetClientCertificate();
                    if (clientCert is not null)
                    {
                        options.ClientCertificates = [clientCert];
                    }

                    // Add node ID header if enrolled
                    if (_options.NodeId.HasValue)
                    {
                        options.Headers["X-Node-Id"] = _options.NodeId.Value.ToString();
                    }
                })
                .WithAutomaticReconnect(new RetryPolicy(_options.ControlPlane))
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                });

            _hubConnection = builder.Build();

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
            await _hubConnection.InvokeAsync("Heartbeat", json, cancellationToken);
            _meter.RecordHeartbeatSent();
            _logger.LogDebug("Heartbeat sent for node {NodeId}", payload.NodeId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to send heartbeat");
            throw;
        }
    }

    public async Task SendCommandResultAsync(CommandResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot send command result: not connected");
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(result);
            await _hubConnection.InvokeAsync("CommandResult", json, cancellationToken);
            _logger.LogDebug("Command result sent for command {CommandId}", result.CommandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command result for {CommandId}", result.CommandId);
            throw;
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
            await _hubConnection.InvokeAsync("Telemetry", json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send telemetry");
            // Telemetry failures are not critical
        }
    }

    private void OnCommandReceived(string commandJson)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CommandEnvelope>(commandJson);
            if (envelope is not null)
            {
                _meter.RecordCommandReceived(envelope.CommandType);
                _logger.LogInformation("Received command {CommandType} with ID {CommandId}",
                    envelope.CommandType, envelope.CommandId);
                CommandReceived?.Invoke(this, new CommandReceivedEventArgs(envelope));
            }
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
