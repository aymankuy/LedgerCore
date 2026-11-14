using LedgerCore.Application.Abstractions.Caching;
using StackExchange.Redis;

namespace LedgerCore.Infrastructure.Caching;

internal sealed class RedisCache(IConnectionMultiplexer redis) : ICache
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
        await redis.GetDatabase().StringGetAsync(key);

    public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken) =>
        redis.GetDatabase().StringSetAsync(key, value, expiry);

    public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
}
