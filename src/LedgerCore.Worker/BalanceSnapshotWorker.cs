using LedgerCore.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace LedgerCore.Worker;

internal sealed class BalanceSnapshotOptions
{
    public const string SectionName = "BalanceSnapshots";

    /// <summary>
    /// How often the snapshots catch up. A balance read scans the entries posted since the last
    /// refresh, so a shorter interval keeps reads cheaper.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Refreshes the balance snapshots once per interval, starting right after startup.</summary>
internal sealed class BalanceSnapshotWorker(
    IServiceScopeFactory scopes,
    IOptions<BalanceSnapshotOptions> options,
    TimeProvider timeProvider,
    ILogger<BalanceSnapshotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var run = await scope.ServiceProvider.GetRequiredService<BalanceSnapshotRefresher>()
                    .RefreshAsync(stoppingToken);

                if (run is null)
                {
                    logger.LogInformation("Another balance snapshot refresh is running; skipped");
                }
                else
                {
                    logger.LogInformation(
                        "Balance snapshots refreshed up to entry {ToEntryId}: {Accounts} accounts updated in {ElapsedMs} ms",
                        run.ToEntryId,
                        run.AccountsUpdated,
                        (run.FinishedAtUtc - run.StartedAtUtc).TotalMilliseconds);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Balances stay correct without a refresh; reads only get slower as entries pile
                // up after the watermark. So the next interval simply tries again.
                logger.LogError(exception, "Balance snapshot refresh failed; retrying in {Delay}", options.Value.Interval);
            }

            await Task.Delay(options.Value.Interval, timeProvider, stoppingToken);
        }
    }
}
