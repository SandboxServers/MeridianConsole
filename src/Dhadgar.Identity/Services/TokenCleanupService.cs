using Dhadgar.Identity.Data;
using Dhadgar.ServiceDefaults.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dhadgar.Identity.Services;

/// <summary>
/// Configuration options for token cleanup service.
/// </summary>
public sealed class TokenCleanupOptions
{
    /// <summary>
    /// How often to run cleanup (default: 6 hours)
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How long to keep revoked tokens before deletion (default: 30 days)
    /// </summary>
    public TimeSpan RevokedTokenRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long to keep expired tokens before deletion (default: 7 days)
    /// </summary>
    public TimeSpan ExpiredTokenRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Batch size for deletion to avoid long transactions (default: 1000)
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether the cleanup service is enabled (default: true)
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Background service that periodically cleans up expired and revoked refresh tokens.
/// Prevents unbounded growth of the refresh_tokens table.
/// </summary>
public sealed class TokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TokenCleanupOptions _options;
    private readonly TimeProvider _timeProvider;

    public TokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<TokenCleanupService> logger,
        IOptions<TokenCleanupOptions> options,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Token cleanup service is disabled");
            return;
        }

        _logger.LogInformation(
            "Token cleanup service started. Interval: {Interval}, RevokedRetention: {RevokedRetention}, ExpiredRetention: {ExpiredRetention}",
            _options.Interval,
            _options.RevokedTokenRetention,
            _options.ExpiredTokenRetention);

        // Initial delay to let the service start up
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupTokensAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token cleanup");
            }

            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Token cleanup service stopped");
    }

    private async Task CleanupTokensAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiredCutoff = now - _options.ExpiredTokenRetention;
        var revokedCutoff = now - _options.RevokedTokenRetention;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var securityLogger = scope.ServiceProvider.GetService<ISecurityEventLogger>();

        // Delete expired tokens older than retention period
        var expiredDeleted = await dbContext.RefreshTokens
            .Where(t => t.ExpiresAt < expiredCutoff)
            .Take(_options.BatchSize)
            .ExecuteDeleteAsync(ct);

        // Delete revoked tokens older than retention period
        var revokedDeleted = await dbContext.RefreshTokens
            .Where(t => t.RevokedAt != null && t.RevokedAt < revokedCutoff)
            .Take(_options.BatchSize)
            .ExecuteDeleteAsync(ct);

        var totalDeleted = expiredDeleted + revokedDeleted;

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Token cleanup completed: {ExpiredDeleted} expired, {RevokedDeleted} revoked tokens deleted",
                expiredDeleted,
                revokedDeleted);
        }
        else
        {
            _logger.LogDebug("Token cleanup completed: no tokens to delete");
        }
    }
}

/// <summary>
/// Extension methods for registering the token cleanup service.
/// </summary>
public static class TokenCleanupServiceExtensions
{
    /// <summary>
    /// Adds the token cleanup background service.
    /// </summary>
    public static IServiceCollection AddTokenCleanupService(
        this IServiceCollection services,
        Action<TokenCleanupOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<TokenCleanupOptions>(_ => { });
        }

        services.AddHostedService<TokenCleanupService>();
        return services;
    }
}
