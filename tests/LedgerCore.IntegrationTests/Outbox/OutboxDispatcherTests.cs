using FluentAssertions;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.Infrastructure.Persistence.Records;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Outbox;

/// <summary>
/// The Worker's dispatcher against a real database. Each test uses its own message type, so
/// messages written by other tests in the same database never reach its handler.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OutboxDispatcherTests(SqlServerFixture fixture)
{
    private const int MaxAttempts = 3;
    private static readonly DateTime OccurredAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _type = $"Test.{Guid.NewGuid():N}";

    [Fact]
    public async Task Each_message_reaches_its_handler_once_and_is_marked_processed()
    {
        var handler = new RecordingHandler(_type);
        var ids = await AddMessagesAsync(3);

        await DispatchUntilEmptyAsync(handler);
        await DispatchUntilEmptyAsync(handler);

        handler.Payloads.Should().BeEquivalentTo(ids.Select(id => id.ToString()));
        (await LoadAsync(ids)).Should().OnlyContain(m => m.ProcessedAtUtc != null && m.Attempts == 0);
    }

    [Fact]
    public async Task A_failing_message_is_retried_until_the_attempt_limit_and_then_left_alone()
    {
        var handler = new RecordingHandler(_type, fail: true);
        var ids = await AddMessagesAsync(1);

        for (var poll = 0; poll < MaxAttempts + 2; poll++)
        {
            await DispatchUntilEmptyAsync(handler);
        }

        handler.Payloads.Should().HaveCount(MaxAttempts);
        var message = (await LoadAsync(ids)).Single();
        message.ProcessedAtUtc.Should().BeNull();
        message.Attempts.Should().Be(MaxAttempts);
    }

    [Fact]
    public async Task A_message_without_a_handler_is_marked_processed()
    {
        var ids = await AddMessagesAsync(1);

        await DispatchUntilEmptyAsync(new RecordingHandler("Some.Other.Type"));

        (await LoadAsync(ids)).Single().ProcessedAtUtc.Should().NotBeNull();
    }

    private async Task DispatchUntilEmptyAsync(IOutboxMessageHandler handler)
    {
        var services = new ServiceCollection()
            .AddInfrastructure(fixture.ConnectionString)
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(handler)
            .Configure<OutboxOptions>(o => o.MaxAttempts = MaxAttempts);
        await using var provider = services.BuildServiceProvider();

        while (true)
        {
            await using var scope = provider.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchBatchAsync(CancellationToken.None) == 0)
            {
                return;
            }
        }
    }

    private async Task<List<Guid>> AddMessagesAsync(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await using var db = fixture.CreateDbContext();
        db.OutboxMessages.AddRange(ids.Select(id => new OutboxMessage
        {
            Id = id,
            Type = _type,
            Payload = id.ToString(),
            OccurredAtUtc = OccurredAt,
        }));
        await db.SaveChangesAsync();
        return ids;
    }

    private async Task<List<OutboxMessage>> LoadAsync(List<Guid> ids)
    {
        await using var db = fixture.CreateDbContext();
        return await db.OutboxMessages.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync();
    }

    private sealed class RecordingHandler(string messageType, bool fail = false) : IOutboxMessageHandler
    {
        public List<string> Payloads { get; } = [];

        public string MessageType => messageType;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            return fail ? throw new InvalidOperationException("Handler failed") : Task.CompletedTask;
        }
    }
}
