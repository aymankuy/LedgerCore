using LedgerCore.Infrastructure.Reporting;
using Microsoft.Azure.Functions.Worker;

namespace LedgerCore.Functions;

/// <summary>
/// Both run at 02:00 UTC, so the serverless database wakes once a night for both. The work
/// lives in Infrastructure, where it is tested against SQL Server and Azurite; these are only
/// the triggers.
/// </summary>
public sealed class NightlyFunctions(NightlyMaintenance maintenance, DailyReconciliation reconciliation)
{
    private const string Nightly = "0 0 2 * * *";

    [Function(nameof(NightlyMaintenance))]
    public Task RunMaintenance([TimerTrigger(Nightly)] TimerInfo timer, CancellationToken cancellationToken) =>
        maintenance.RunAsync(cancellationToken);

    [Function(nameof(DailyReconciliation))]
    public Task RunReconciliation([TimerTrigger(Nightly)] TimerInfo timer, CancellationToken cancellationToken) =>
        reconciliation.RunAsync(cancellationToken);
}
