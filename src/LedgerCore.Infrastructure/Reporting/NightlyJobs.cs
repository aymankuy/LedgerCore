using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace LedgerCore.Infrastructure.Reporting;

/// <summary>
/// What the Worker does continuously, done once a night instead (ADR 0011). On Azure a
/// per-minute job would keep the free serverless database from ever pausing.
/// </summary>
public sealed class NightlyMaintenance(
    BalanceSnapshotRefresher snapshots,
    IdempotencyKeyCleaner idempotencyKeys,
    OutboxDispatcher outbox,
    TimeProvider timeProvider,
    ILogger<NightlyMaintenance> logger)
{
    // ADR 0005: a key protects against retries for 24 hours.
    private static readonly TimeSpan IdempotencyKeyRetention = TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var run = await snapshots.RefreshAsync(cancellationToken);
        logger.LogInformation("Balance snapshots refreshed up to entry {ToEntryId}, {Accounts} accounts updated",
            run?.ToEntryId, run?.AccountsUpdated);

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - IdempotencyKeyRetention;
        var deleted = await idempotencyKeys.DeleteOlderThanAsync(cutoff, cancellationToken);
        logger.LogInformation("Deleted {Count} idempotency keys created before {Cutoff:O}", deleted, cutoff);

        // Nothing consumes the outbox on Azure, because there is no Redis cache there; draining
        // it keeps the filtered index small, and a consumer added later finds nothing stale.
        int dispatched, total = 0;
        while ((dispatched = await outbox.DispatchBatchAsync(cancellationToken)) > 0)
        {
            total += dispatched;
        }

        logger.LogInformation("Drained {Count} outbox messages", total);
    }
}

/// <summary>Builds yesterday's reconciliation report and stores it.</summary>
public sealed class DailyReconciliation(
    ReconciliationReporter reporter,
    ReconciliationReportStore store,
    TimeProvider timeProvider,
    ILogger<DailyReconciliation> logger)
{
    public async Task<ReconciliationReport> RunAsync(CancellationToken cancellationToken)
    {
        var yesterday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);
        var report = await reporter.CreateAsync(yesterday, cancellationToken);
        var blob = await store.SaveAsync(report, cancellationToken);

        if (report.IsBalanced)
        {
            logger.LogInformation("Reconciliation for {Day} is balanced; written to {Blob}", yesterday, blob);
        }
        else
        {
            logger.LogError(
                "Reconciliation for {Day} FAILED: {Unbalanced} unbalanced transactions, {Drifted} drifted accounts; written to {Blob}",
                yesterday, report.UnbalancedTransactionCount, report.DriftedAccountCount, blob);
        }

        return report;
    }
}
