using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedgerCore.Infrastructure.Outbox;

/// <summary>
/// Hands unprocessed outbox messages to their handlers and marks them processed. Several Workers
/// may run at once: each claims its batch with UPDLOCK and skips rows another one has claimed
/// (READPAST), so no message is handled by two dispatchers at the same time.
/// </summary>
public sealed class OutboxDispatcher(
    LedgerDbContext db,
    IEnumerable<IOutboxMessageHandler> handlers,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    private readonly Dictionary<string, IOutboxMessageHandler> _handlers = handlers.ToDictionary(h => h.MessageType);
    private readonly OutboxOptions _options = options.Value;

    /// <summary>Dispatches one batch, oldest first. Returns how many messages it claimed.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The filtered index IX_OutboxMessages_Unprocessed covers exactly these rows.
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT TOP ({_options.BatchSize}) *
                FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ProcessedAtUtc IS NULL AND Attempts < {_options.MaxAttempts}
                ORDER BY OccurredAtUtc
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            await DispatchAsync(message, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // A message nobody handles yet is still processed: there is simply nothing to do.
            if (_handlers.TryGetValue(message.Type, out var handler))
            {
                await handler.HandleAsync(message.Payload, cancellationToken);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            await db.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, now), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One bad message must not block the others; it is retried on a later poll.
            await db.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, m => m.Attempts + 1), cancellationToken);

            var attempts = message.Attempts + 1;
            if (attempts >= _options.MaxAttempts)
            {
                logger.LogError(exception, "Outbox message {MessageId} ({MessageType}) failed {Attempts} times and will not be retried",
                    message.Id, message.Type, attempts);
            }
            else
            {
                logger.LogWarning(exception, "Outbox message {MessageId} ({MessageType}) failed, attempt {Attempts} of {MaxAttempts}",
                    message.Id, message.Type, attempts, _options.MaxAttempts);
            }
        }
    }
}
