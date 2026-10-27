using Dhadgar.Agent.Core.Communication;
using Dhadgar.Agent.Core.Configuration;
using Dhadgar.Agent.Core.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Agent.Core.Health;

/// <summary>
/// Background service that sends periodic heartbeats to the control plane.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly IControlPlaneClient _controlPlaneClient;
    private readonly IHealthReporter _healthReporter;
    private readonly IAgentIdentityProvider _identity;
    private readonly IOptionsMonitor<AgentOptions> _optionsMonitor;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IControlPlaneClient controlPlaneClient,
        IHealthReporter healthReporter,
        IAgentIdentityProvider identity,
        IOptionsMonitor<AgentOptions> optionsMonitor,
        ILogger<HeartbeatService> logger)
    {
        _controlPlaneClient = controlPlaneClient ?? throw new ArgumentNullException(nameof(controlPlaneClient));
        _healthReporter = healthReporter ?? throw new ArgumentNullException(nameof(healthReporter));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat service started");

        // Wait until an identity is set (enrollment or the state store). Waiting on the provider
        // rather than AgentOptions.NodeId is the #101 fix: options never gain a NodeId at runtime.
        try
        {
            await _identity.WaitForIdentityAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Heartbeat service stopped before an identity was set");
            return;
        }

        var interval = TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.ControlPlane.HeartbeatIntervalSeconds);

        // Initial delay to let other services start
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send heartbeat");
                // Don't exit the loop on failure, just try again next interval
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Heartbeat service stopped");
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        // Only send heartbeat if connected
        if (_controlPlaneClient.State != ConnectionState.Connected)
        {
            _logger.LogDebug("Skipping heartbeat: not connected to control plane");
            return;
        }

        var payloadResult = await _healthReporter.GetHeartbeatPayloadAsync(cancellationToken);
        if (payloadResult.IsFailure)
        {
            _logger.LogWarning("Skipping heartbeat: {Error}", payloadResult.Error);
            return;
        }

        var payload = payloadResult.Value;
        var sendResult = await _controlPlaneClient.SendHeartbeatAsync(
            HeartbeatPayloadMapper.ToContract(payload), cancellationToken);
        if (sendResult.IsFailure)
        {
            _logger.LogWarning("Heartbeat not sent: {Error}", sendResult.Error);
            return;
        }

        if (!sendResult.Value.Acknowledged)
        {
            _logger.LogWarning(
                "Heartbeat not acknowledged by control plane. Error code: {ErrorCode}",
                sendResult.Value.ErrorCode);
        }

        _logger.LogDebug(
            "Heartbeat sent. Status: {Status}, Active processes: {ProcessCount}",
            payload.Status,
            payload.ActiveProcesses.Count);
    }
}
