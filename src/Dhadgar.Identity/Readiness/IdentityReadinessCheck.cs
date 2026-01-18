using Dhadgar.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Dhadgar.Identity.Readiness;

public sealed class IdentityReadinessCheck : IHealthCheck
{
    private readonly IdentityDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdentityReadinessCheck> _logger;

    public IdentityReadinessCheck(
        IdentityDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<IdentityReadinessCheck> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, object>();

        var dbReady = await CheckPostgresAsync(details, cancellationToken);
        var redisReady = await CheckRedisAsync(details, cancellationToken);

        return dbReady && redisReady
            ? HealthCheckResult.Healthy(data: details)
            : HealthCheckResult.Unhealthy(data: details);
    }

    private async Task<bool> CheckPostgresAsync(Dictionary<string, object> details, CancellationToken ct)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(ct);
            details["postgres"] = canConnect ? "ok" : "unavailable";
            return canConnect;
        }
        catch (Exception ex)
        {
            details["postgres"] = "error";
            details["postgres_error"] = ex.Message;
            _logger.LogWarning(ex, "Postgres readiness check failed.");
            return false;
        }
    }

    private async Task<bool> CheckRedisAsync(Dictionary<string, object> details, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync();
            details["redis"] = "ok";
            details["redis_latency_ms"] = latency.TotalMilliseconds;
            return true;
        }
        catch (Exception ex)
        {
            details["redis"] = "error";
            details["redis_error"] = ex.Message;
            _logger.LogWarning(ex, "Redis readiness check failed.");
            return false;
        }
    }
}
