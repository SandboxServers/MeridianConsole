using Dhadgar.Identity.Data;
using Dhadgar.ServiceDefaults.Readiness;
using Microsoft.EntityFrameworkCore;

namespace Dhadgar.Identity.Readiness;

public sealed class IdentityReadinessCheck : IReadinessCheck
{
    private readonly IdentityDbContext _dbContext;
    private readonly IRedisReadinessProbe _redisProbe;
    private readonly ILogger<IdentityReadinessCheck> _logger;

    public IdentityReadinessCheck(
        IdentityDbContext dbContext,
        IRedisReadinessProbe redisProbe,
        ILogger<IdentityReadinessCheck> logger)
    {
        _dbContext = dbContext;
        _redisProbe = redisProbe;
        _logger = logger;
    }

    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        var details = new Dictionary<string, object?>();

        var dbReady = await CheckPostgresAsync(details, ct);
        var redisReady = await CheckRedisAsync(details, ct);

        return dbReady && redisReady
            ? ReadinessResult.Ready(details)
            : ReadinessResult.NotReady(details);
    }

    private async Task<bool> CheckPostgresAsync(Dictionary<string, object?> details, CancellationToken ct)
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

    private async Task<bool> CheckRedisAsync(Dictionary<string, object?> details, CancellationToken ct)
    {
        var result = await _redisProbe.CheckAsync(ct);
        if (result.IsReady)
        {
            details["redis"] = "ok";
            if (result.LatencyMs is not null)
            {
                details["redis_latency_ms"] = result.LatencyMs;
            }
            return true;
        }

        details["redis"] = "error";
        details["redis_error"] = result.Error ?? "unknown";
        _logger.LogWarning("Redis readiness check failed: {Error}", result.Error);
        return false;
    }
}
