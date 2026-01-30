using Dhadgar.Nodes.Services;
using Microsoft.Extensions.Options;

namespace Dhadgar.Nodes.BackgroundServices;

/// <summary>
/// Background service that periodically checks for and expires stale capacity reservations.
/// </summary>
public sealed class ReservationExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpiryService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeProvider _timeProvider;

    public ReservationExpiryService(
        IServiceScopeFactory scopeFactory,
        IOptions<NodesOptions> options,
        ILogger<ReservationExpiryService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _checkInterval = TimeSpan.FromMinutes(options.Value.ReservationExpiryCheckIntervalMinutes);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reservation expiry service started with interval {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error expiring stale reservations");
            }

            try
            {
                await Task.Delay(_checkInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Reservation expiry service stopped");
    }

    private async Task ExpireReservationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reservationService = scope.ServiceProvider.GetRequiredService<ICapacityReservationService>();

        var expiredCount = await reservationService.ExpireStaleReservationsAsync(ct);

        if (expiredCount > 0)
        {
            _logger.LogInformation("Expired {Count} stale capacity reservations", expiredCount);
        }
    }
}
