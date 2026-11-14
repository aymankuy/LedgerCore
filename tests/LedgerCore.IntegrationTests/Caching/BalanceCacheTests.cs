using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LedgerCore.Api.Controllers;
using LedgerCore.Application.Abstractions.Caching;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Domain.Ledger;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Infrastructure.Persistence.Records;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace LedgerCore.IntegrationTests.Caching;

/// <summary>
/// The balance cache with a real Redis: the API fills it, and only the Worker's outbox handler
/// clears it. Between a posting and the next dispatch the cached balance is stale, by design.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BalanceCacheTests(SqlServerFixture fixture, RedisFixture redis) : IClassFixture<RedisFixture>
{
    [Fact]
    public async Task A_cached_balance_is_served_until_the_Worker_dispatches_the_posting()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, redis.ConnectionString);
        var client = await api.CreateUserClientAsync();
        var accountId = await OpenAccountAsync(client);
        await DepositAsync(client, accountId, 100m);
        await DispatchOutboxAsync();

        (await GetBalanceAsync(client, accountId)).Should().Be(100m);
        (await redis.Database.KeyExistsAsync(CacheKeys.AccountBalance(accountId))).Should().BeTrue();

        await DepositAsync(client, accountId, 50m);
        var beforeDispatch = await GetBalanceAsync(client, accountId);
        await DispatchOutboxAsync();
        var afterDispatch = await GetBalanceAsync(client, accountId);

        beforeDispatch.Should().Be(100m, "the posting is committed, but its event has not been dispatched yet");
        afterDispatch.Should().Be(150m);
    }

    [Fact]
    public async Task A_posting_removes_the_cached_balances_of_its_accounts_and_no_others()
    {
        var (payer, payee, bystander) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var id in new[] { payer, payee, bystander })
        {
            await redis.Database.StringSetAsync(CacheKeys.AccountBalance(id), "{}", TimeSpan.FromMinutes(1));
        }

        await AddPostedEventAsync(new LedgerTransactionPosted(Guid.NewGuid(), TransactionType.Transfer, [payer, payee]));
        await DispatchOutboxAsync();

        (await redis.Database.KeyExistsAsync(CacheKeys.AccountBalance(payer))).Should().BeFalse();
        (await redis.Database.KeyExistsAsync(CacheKeys.AccountBalance(payee))).Should().BeFalse();
        (await redis.Database.KeyExistsAsync(CacheKeys.AccountBalance(bystander))).Should().BeTrue();
    }

    [Fact]
    public async Task Cached_balances_expire_even_if_no_event_removes_them()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, redis.ConnectionString);
        var client = await api.CreateUserClientAsync();
        var accountId = await OpenAccountAsync(client);

        await GetBalanceAsync(client, accountId);

        var ttl = await redis.Database.KeyTimeToLiveAsync(CacheKeys.AccountBalance(accountId));
        ttl.Should().NotBeNull().And.BeLessThanOrEqualTo(new GetAccountBalanceQuery(accountId).CacheDuration);
    }

    /// <summary>What the Worker does on each poll, with the same wiring as its Program.cs.</summary>
    private async Task DispatchOutboxAsync()
    {
        var services = new ServiceCollection()
            .AddInfrastructure(fixture.ConnectionString)
            .AddRedisCache(redis.ConnectionString)
            .AddLogging()
            .AddSingleton(TimeProvider.System);
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

    private async Task AddPostedEventAsync(LedgerTransactionPosted posted)
    {
        await using var db = fixture.CreateDbContext();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(LedgerTransactionPosted),
            Payload = JsonSerializer.Serialize(posted),
            OccurredAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> OpenAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { currency = "EUR" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AccountCreatedResponse>())!.AccountId;
    }

    private static async Task DepositAsync(HttpClient client, Guid accountId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<decimal> GetBalanceAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{accountId}/balance"))!.Balance;
}

/// <summary>One Redis container for the cache tests.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();
    private ConnectionMultiplexer? _connection;

    public string ConnectionString => _container.GetConnectionString();

    public IDatabase Database => _connection!.GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
