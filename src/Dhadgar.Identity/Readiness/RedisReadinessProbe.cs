using StackExchange.Redis;

namespace Dhadgar.Identity.Readiness;

public interface IRedisReadinessProbe
{
    Task<RedisReadinessResult> CheckAsync(CancellationToken ct);
}

public sealed record RedisReadinessResult(bool IsReady, double? LatencyMs, string? Error)
{
    public static RedisReadinessResult Ready(TimeSpan latency)
        => new(true, latency.TotalMilliseconds, null);

    public static RedisReadinessResult NotReady(string error)
        => new(false, null, error);
}

public sealed class RedisReadinessProbe : IRedisReadinessProbe
{
    private readonly IConnectionMultiplexer _redis;

    public RedisReadinessProbe(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<RedisReadinessResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            var latency = await _redis.GetDatabase().PingAsync();
            return RedisReadinessResult.Ready(latency);
        }
        catch (Exception ex)
        {
            return RedisReadinessResult.NotReady(ex.Message);
        }
    }
}
