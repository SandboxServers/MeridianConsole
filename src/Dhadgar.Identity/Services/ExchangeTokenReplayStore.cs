using StackExchange.Redis;

namespace Dhadgar.Identity.Services;

public interface IExchangeTokenReplayStore
{
    Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default);
}

public sealed class RedisExchangeTokenReplayStore : IExchangeTokenReplayStore
{
    private readonly IConnectionMultiplexer _connection;

    public RedisExchangeTokenReplayStore(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<bool> MarkAsUsedAsync(string jti, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _connection.GetDatabase();
        var key = $"exchange_token:{jti}";
        return await db.StringSetAsync(key, "used", ttl, when: When.NotExists);
    }
}
