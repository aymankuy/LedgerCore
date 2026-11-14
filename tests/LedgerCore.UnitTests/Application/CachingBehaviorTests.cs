using LedgerCore.Application.Abstractions.Caching;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Ledger.Deposit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LedgerCore.UnitTests.Application;

public class CachingBehaviorTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly GetAccountBalanceQuery Query = new(AccountId);
    private static readonly AccountBalanceResponse FromDatabase = new(AccountId, "EUR", 100m);

    private readonly ICache _cache = Substitute.For<ICache>();
    private int _handlerCalls;

    private CachingBehavior<GetAccountBalanceQuery, AccountBalanceResponse> Behavior =>
        new(_cache, NullLogger<CachingBehavior<GetAccountBalanceQuery, AccountBalanceResponse>>.Instance);

    private RequestHandlerDelegate<AccountBalanceResponse> Handler => _ =>
    {
        _handlerCalls++;
        return Task.FromResult(FromDatabase);
    };

    [Fact]
    public async Task A_cached_response_is_returned_without_running_the_query()
    {
        _cache.GetAsync(Query.CacheKey, Arg.Any<CancellationToken>())
            .Returns($$"""{"AccountId":"{{AccountId}}","Currency":"EUR","Balance":42.5}""");

        var response = await Behavior.Handle(Query, Handler, CancellationToken.None);

        response.Should().Be(new AccountBalanceResponse(AccountId, "EUR", 42.5m));
        _handlerCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_miss_runs_the_query_and_stores_the_response_with_the_query_expiry()
    {
        var response = await Behavior.Handle(Query, Handler, CancellationToken.None);

        response.Should().Be(FromDatabase);
        _handlerCalls.Should().Be(1);
        await _cache.Received(1).SetAsync(
            CacheKeys.AccountBalance(AccountId),
            Arg.Is<string>(json => json.Contains("\"Balance\":100")),
            Query.CacheDuration,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_request_that_is_not_a_cacheable_query_never_touches_the_cache()
    {
        var behavior = new CachingBehavior<DepositCommand, Guid>(
            _cache, NullLogger<CachingBehavior<DepositCommand, Guid>>.Instance);

        await behavior.Handle(new DepositCommand(AccountId, 10m, null), _ => Task.FromResult(Guid.NewGuid()), CancellationToken.None);

        _cache.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_cache_is_down_the_query_still_answers_from_the_database()
    {
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("Redis is down"));
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Redis is down"));

        var response = await Behavior.Handle(Query, Handler, CancellationToken.None);

        response.Should().Be(FromDatabase);
        _handlerCalls.Should().Be(1);
    }

    [Fact]
    public void The_balance_key_is_the_same_for_the_query_and_the_invalidator()
    {
        Query.CacheKey.Should().Be(CacheKeys.AccountBalance(AccountId));
        Query.CacheDuration.Should().BePositive();
    }
}
