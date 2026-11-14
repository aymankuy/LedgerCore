namespace LedgerCore.Application.Abstractions.Caching;

/// <summary>A string key/value cache with expiry. Redis in production; nothing at all when unconfigured.</summary>
public interface ICache
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken);

    Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

/// <summary>The cache used when none is configured: every read misses, every write is dropped.</summary>
internal sealed class NoCache : ICache
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) => Task.CompletedTask;
}
