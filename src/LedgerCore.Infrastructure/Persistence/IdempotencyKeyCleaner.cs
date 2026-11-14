using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence;

/// <summary>Deletes idempotency keys older than the retention period (24 hours, ADR 0005).</summary>
public sealed class IdempotencyKeyCleaner(LedgerDbContext db)
{
    // Small batches keep each DELETE short, so it never holds locks that a request claiming a
    // new key would have to wait for.
    private const int BatchSize = 1_000;

    /// <summary>Deletes every key created before <paramref name="cutoffUtc"/>. Returns how many were deleted.</summary>
    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;

        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                $"DELETE TOP ({BatchSize}) FROM dbo.IdempotencyKeys WHERE CreatedAtUtc < {cutoffUtc}",
                cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        return total;
    }
}
