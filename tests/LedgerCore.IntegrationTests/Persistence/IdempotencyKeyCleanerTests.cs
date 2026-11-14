using FluentAssertions;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Records;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class IdempotencyKeyCleanerTests(SqlServerFixture fixture)
{
    // Long before anything other tests write, so the clean-up here never touches their keys.
    private static readonly DateTime Cutoff = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Keys_older_than_the_cutoff_are_deleted_in_batches_and_newer_ones_kept()
    {
        var prefix = Guid.NewGuid().ToString("N");
        await using (var db = fixture.CreateDbContext())
        {
            // More than one batch of 1,000, so the loop has to run more than once.
            db.IdempotencyKeys.AddRange(Enumerable.Range(0, 1_500).Select(i => Key($"{prefix}:old:{i}", Cutoff.AddDays(-1))));
            db.IdempotencyKeys.Add(Key($"{prefix}:new", Cutoff.AddSeconds(1)));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var deleted = await new IdempotencyKeyCleaner(db).DeleteOlderThanAsync(Cutoff, CancellationToken.None);
            deleted.Should().Be(1_500);
        }

        await using (var db = fixture.CreateDbContext())
        {
            var left = await db.IdempotencyKeys.Where(k => k.Key.StartsWith(prefix)).Select(k => k.Key).ToListAsync();
            left.Should().Equal($"{prefix}:new");
        }
    }

    private static IdempotencyRecord Key(string key, DateTime createdAtUtc) => new()
    {
        Key = key,
        RequestHash = new string('0', 64),
        CreatedAtUtc = createdAtUtc,
    };
}
