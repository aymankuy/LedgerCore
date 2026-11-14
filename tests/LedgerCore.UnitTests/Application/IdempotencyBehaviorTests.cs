using System.Text.Json;
using FluentValidation;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Deposit;
using MediatR;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class IdempotencyBehaviorTests
{
    private readonly IIdempotencyContext _context = Substitute.For<IIdempotencyContext>();
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _handlerResult = Guid.NewGuid();
    private readonly DepositCommand _deposit = new(Guid.NewGuid(), 10m);
    private int _handlerCalls;

    public IdempotencyBehaviorTests()
    {
        _context.Key.Returns("key-1");
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_userId);
    }

    [Fact]
    public async Task A_new_key_runs_the_handler_once_and_stores_its_response()
    {
        var result = await Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        result.Should().Be(_handlerResult);
        _handlerCalls.Should().Be(1);
        await _store.Received(1).SaveResponseAsync(
            $"{_userId:N}:key-1", JsonSerializer.Serialize(_handlerResult), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_repeated_key_with_the_same_request_returns_the_stored_response_without_running_the_handler()
    {
        var firstResult = Guid.NewGuid();
        _store.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new StoredRequest(call.ArgAt<string>(1), JsonSerializer.Serialize(firstResult)));

        var result = await Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        result.Should().Be(firstResult);
        _handlerCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_repeated_key_with_a_different_request_is_a_conflict()
    {
        _store.TryClaimAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredRequest(new string('0', 64), "\"00000000-0000-0000-0000-000000000000\""));

        var act = () => Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        _handlerCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_key_is_a_validation_error(string? key)
    {
        _context.Key.Returns(key);

        var act = () => Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _handlerCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_key_longer_than_the_limit_is_a_validation_error()
    {
        _context.Key.Returns(new string('k', IIdempotencyContext.MaxKeyLength + 1));

        var act = () => Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Keys_without_a_signed_in_user_share_the_anonymous_scope()
    {
        _currentUser.IsAuthenticated.Returns(false);

        await Behavior<DepositCommand, Guid>().Handle(_deposit, Next(), CancellationToken.None);

        await _store.Received(1).TryClaimAsync("anonymous:key-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Requests_that_are_not_idempotent_commands_pass_straight_through()
    {
        _context.Key.Returns((string?)null);

        await Behavior<GetAccountBalanceQuery, AccountBalanceResponse>().Handle(
            new GetAccountBalanceQuery(Guid.NewGuid()),
            _ => Task.FromResult(new AccountBalanceResponse(Guid.NewGuid(), "EUR", 0m)),
            CancellationToken.None);

        await _store.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, default!, default);
    }

    private IdempotencyBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>()
        where TRequest : notnull =>
        new(_context, _store, _currentUser);

    private RequestHandlerDelegate<Guid> Next() => _ =>
    {
        _handlerCalls++;
        return Task.FromResult(_handlerResult);
    };
}
