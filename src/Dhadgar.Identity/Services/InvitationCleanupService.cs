using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Configuration options for the invitation cleanup background service.
/// </summary>
public sealed class InvitationCleanupOptions
{
    /// <summary>
    /// How often to run the cleanup task. Default is 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to enable the cleanup service. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Background service that periodically marks expired invitations as rejected.
/// </summary>
public sealed class InvitationCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvitationCleanupService> _logger;
    private readonly InvitationCleanupOptions _options;

    public InvitationCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<InvitationCleanupService> logger,
        IOptions<InvitationCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Invitation cleanup service is disabled");
            return;
        }

        _logger.LogInformation(
            "Invitation cleanup service started with interval {Interval}",
            _options.CleanupInterval);

        // Wait a bit before first run to allow service to stabilize
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredInvitationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during invitation cleanup");
            }

            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Invitation cleanup service stopped");
    }

    private async Task CleanupExpiredInvitationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var membershipService = scope.ServiceProvider.GetRequiredService<MembershipService>();

        var expiredCount = await membershipService.MarkExpiredInvitationsAsync(ct);

        if (expiredCount > 0)
        {
            _logger.LogInformation("Marked {Count} expired invitations", expiredCount);
        }
        else
        {
            _logger.LogDebug("No expired invitations to clean up");
        }
    }
}

/// <summary>
/// Extension methods for configuring invitation cleanup service.
/// </summary>
public static class InvitationCleanupServiceExtensions
{
    /// <summary>
    /// Adds the invitation cleanup background service.
    /// </summary>
    public static IServiceCollection AddInvitationCleanupService(
        this IServiceCollection services,
        Action<InvitationCleanupOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<InvitationCleanupOptions>(_ => { });
        }

        services.AddHostedService<InvitationCleanupService>();
        return services;
    }
}
