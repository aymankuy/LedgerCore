using LedgerCore.Application.Abstractions.Idempotency;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence;

internal sealed class IdempotencyStore(LedgerDbContext db, TimeProvider timeProvider) : IIdempotencyStore
{
    // Primary key violation: the key is already in the table.
    private const int DuplicateKeyError = 2627;

    public async Task<StoredRequest?> TryClaimAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An idempotency key can only be claimed inside a transaction.");
        }

        try
        {
            // If another open transaction inserted the same key, this INSERT waits on that row's
            // lock. It fails as a duplicate if the other one commits, and succeeds if it rolls back.
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO dbo.IdempotencyKeys ([Key], RequestHash, CreatedAtUtc) VALUES ({key}, {requestHash}, {timeProvider.GetUtcNow().UtcDateTime})",
                cancellationToken);
            return null;
        }
        catch (SqlException e) when (e.Number == DuplicateKeyError)
        {
            return await db.IdempotencyKeys
                .AsNoTracking()
                .Where(r => r.Key == key)
                .Select(r => new StoredRequest(r.RequestHash, r.ResponseBody))
                .SingleAsync(cancellationToken);
        }
    }

    public Task SaveResponseAsync(string key, string responseJson, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"UPDATE dbo.IdempotencyKeys SET ResponseBody = {responseJson} WHERE [Key] = {key}",
            cancellationToken);
}
