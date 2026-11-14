namespace LedgerCore.Application.Abstractions.Caching;

/// <summary>
/// A query whose response the caching behavior may serve from the cache. Only queries: a
/// command never implements this, so no money movement ever reads a cached balance.
/// </summary>
public interface ICacheableQuery
{
    string CacheKey { get; }

    /// <summary>
    /// An upper bound on staleness. Entries are normally removed as soon as the Worker sees the
    /// ledger change; the expiry covers the case where that removal is missed.
    /// </summary>
    TimeSpan CacheDuration { get; }
}

public static class CacheKeys
{
    /// <summary>Shared by the balance query, which fills the entry, and the Worker, which removes it.</summary>
    public static string AccountBalance(Guid accountId) => $"ledgercore:balance:{accountId:N}";
}
