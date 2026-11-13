using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace LedgerCore.Infrastructure.Persistence;

/// <summary>
/// Retries a whole transaction when SQL Server picks it as a deadlock victim. Lock ordering
/// prevents the deadlocks we know about; this is the safety net for the ones we do not.
/// </summary>
internal static class DeadlockRetry
{
    // The server has already rolled the victim's transaction back, so running it again is safe.
    private const int DeadlockVictimError = 1205;

    public static ResiliencePipeline Pipeline { get; } = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqlException>(e => e.Number == DeadlockVictimError),
            MaxRetryAttempts = 3,

            // About 50, 100 and 200 ms. The jitter spreads the retries out, so two victims of the
            // same deadlock do not run again at the same moment and collide once more.
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(50),
            UseJitter = true,
        })
        .Build();
}
