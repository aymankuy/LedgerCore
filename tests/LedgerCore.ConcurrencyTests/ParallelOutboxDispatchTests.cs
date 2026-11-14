using System.Collections.Concurrent;
using FluentAssertions;
using LedgerCore.ConcurrencyTests.Infrastructure;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.ConcurrencyTests;

[Collection(ConcurrencyCollection.Name)]
public sealed class ParallelOutboxDispatchTests(ConcurrencyFixture fixture)
{
    private const int Messages = 500;
    private const int Dispatchers = 4;

    [Fact]
    public async Task Four_dispatchers_in_parallel_handle_every_message_exactly_once()
    {
        var type = $"Test.{Guid.NewGuid():N}";
        var handled = new ConcurrentBag<string>();
        var ids = await AddMessagesAsync(type);

        await using var provider = new ServiceCollection()
            .AddInfrastructure(fixture.ConnectionString)
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IOutboxMessageHandler>(new RecordingHandler(type, handled))
            .Configure<OutboxOptions>(o => o.BatchSize = 20)
            .BuildServiceProvider();

        // Each dispatcher keeps claiming batches until it finds nothing left. READPAST makes
        // them skip each other's claimed rows instead of waiting for them or taking them too.
        await ParallelRun.AllAtOnceAsync(Dispatchers, async _ =>
        {
            while (true)
            {
                await using var scope = provider.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None) == 0)
                {
                    return true;
                }
            }
        });

        handled.Should().HaveCount(Messages).And.OnlyHaveUniqueItems();
        handled.Should().BeEquivalentTo(ids.Select(id => id.ToString()));

        await using var db = CreateDbContext();
        (await db.OutboxMessages.CountAsync(m => m.Type == type && m.ProcessedAtUtc == null)).Should().Be(0);
    }

    private async Task<List<Guid>> AddMessagesAsync(string type)
    {
        var ids = Enumerable.Range(0, Messages).Select(_ => Guid.NewGuid()).ToList();
        await using var db = CreateDbContext();
        var occurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.OutboxMessages.AddRange(ids.Select((id, i) => new OutboxMessage
        {
            Id = id,
            Type = type,
            Payload = id.ToString(),
            OccurredAtUtc = occurredAt.AddMilliseconds(i),
        }));
        await db.SaveChangesAsync();
        return ids;
    }

    private LedgerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    private sealed class RecordingHandler(string messageType, ConcurrentBag<string> handled) : IOutboxMessageHandler
    {
        public string MessageType => messageType;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            handled.Add(payload);
            return Task.CompletedTask;
        }
    }
}
