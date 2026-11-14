using LedgerCore.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace LedgerCore.Worker;

internal sealed class IdempotencyCleanupOptions
{
    public const string SectionName = "IdempotencyCleanup";

    /// <summary>How long a key keeps protecting against retries (ADR 0005).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Deletes expired idempotency keys once per interval, starting right after startup.</summary>
internal sealed class IdempotencyCleanupWorker(
    IServiceScopeFactory scopes,
    IOptions<IdempotencyCleanupOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyCleanupWorker> logger) : BackgroundService
{
    // A failed run, for example while the API is still migrating the database at startup, is
    // retried after a minute instead of waiting a whole interval.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = options.Value.Interval;

            try
            {
                var cutoff = timeProvider.GetUtcNow().UtcDateTime - options.Value.Retention;
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<IdempotencyKeyCleaner>()
                    .DeleteOlderThanAsync(cutoff, stoppingToken);

                logger.LogInformation("Deleted {Count} idempotency keys created before {Cutoff:O}", deleted, cutoff);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Idempotency key clean-up failed; retrying in {Delay}", RetryDelay);
                delay = RetryDelay;
            }

            await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }
}
