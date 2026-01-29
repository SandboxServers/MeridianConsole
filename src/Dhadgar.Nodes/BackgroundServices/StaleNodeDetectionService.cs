using Dhadgar.Nodes.Services;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.BackgroundServices;

/// <summary>
/// Background service that periodically checks for nodes that haven't sent heartbeats
/// and marks them as offline.
/// </summary>
public sealed class StaleNodeDetectionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleNodeDetectionService> _logger;
    private readonly TimeSpan _checkInterval;

    public StaleNodeDetectionService(
        IServiceScopeFactory scopeFactory,
        IOptions<NodesOptions> options,
        ILogger<StaleNodeDetectionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = TimeSpan.FromMinutes(options.Value.StaleNodeCheckIntervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stale node detection service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForStaleNodesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for stale nodes");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Stale node detection service stopped");
    }

    private async Task CheckForStaleNodesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var heartbeatService = scope.ServiceProvider.GetRequiredService<IHeartbeatService>();

        var staleCount = await heartbeatService.CheckStaleNodesAsync(ct);

        if (staleCount > 0)
        {
            _logger.LogInformation("Marked {Count} nodes as offline due to heartbeat timeout", staleCount);
        }
    }
}
