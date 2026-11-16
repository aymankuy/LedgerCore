using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence;

/// <summary>Folds the entries posted since the last refresh into the balance snapshots (ADR 0009).</summary>
public sealed class BalanceSnapshotRefresher(LedgerDbContext db)
{
    /// <summary>
    /// Runs one refresh and returns its logged run. Returns null if another refresh was already
    /// running, in which case it does nothing.
    /// </summary>
    public async Task<BalanceSnapshotRun?> RefreshAsync(CancellationToken cancellationToken)
    {
        // The first run on a large ledger folds its whole history; later runs take milliseconds.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var runs = await db.BalanceSnapshotRuns
            .FromSqlRaw("EXEC dbo.usp_RefreshBalanceSnapshots")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return runs.SingleOrDefault();
    }
}
