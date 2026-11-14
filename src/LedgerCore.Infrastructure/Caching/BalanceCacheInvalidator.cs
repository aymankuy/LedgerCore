using System.Text.Json;
using LedgerCore.Application.Abstractions.Caching;
using LedgerCore.Domain.Ledger;
using LedgerCore.Infrastructure.Outbox;

namespace LedgerCore.Infrastructure.Caching;

/// <summary>
/// Runs in the Worker: when a posting changes accounts, their cached balances are removed, so
/// the next read goes to the ledger. Removing a key twice is harmless, which makes this safe
/// under the outbox's at-least-once delivery.
/// </summary>
internal sealed class BalanceCacheInvalidator(ICache cache) : IOutboxMessageHandler
{
    public string MessageType => nameof(LedgerTransactionPosted);

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var posted = JsonSerializer.Deserialize<LedgerTransactionPosted>(payload)
            ?? throw new InvalidOperationException("The LedgerTransactionPosted payload is empty.");

        return cache.RemoveAsync(posted.AccountIds.Select(CacheKeys.AccountBalance).ToList(), cancellationToken);
    }
}
