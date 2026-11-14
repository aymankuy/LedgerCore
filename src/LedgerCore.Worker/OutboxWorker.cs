using LedgerCore.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace LedgerCore.Worker;

/// <summary>
/// Polls the outbox and dispatches what it finds. When a batch comes back full it goes again
/// at once, so a backlog drains without waiting for the next tick.
/// </summary>
internal sealed class OutboxWorker(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);

        do
        {
            try
            {
                int claimed;
                do
                {
                    // A fresh scope, and so a fresh DbContext, per batch.
                    await using var scope = scopes.CreateAsyncScope();
                    claimed = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(stoppingToken);
                }
                while (claimed == options.Value.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // For example the database is not reachable yet. Keep polling; the messages wait.
                logger.LogError(exception, "Outbox dispatch failed; retrying on the next poll");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
