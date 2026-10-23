using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Accounts.OpenAccount;
using LedgerCore.Application.Behaviors;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.Domain.Accounts;
using LedgerCore.UnitTests.Domain;
using MediatR;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class AccountAccessBehaviorTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly Account _account = TestAccounts.Customer();
    private int _handlerCalls;

    public AccountAccessBehaviorTests()
    {
        _accounts.GetByIdAsync(_account.Id, Arg.Any<CancellationToken>()).Returns(_account);
    }

    [Fact]
    public async Task Owner_reaches_the_handler()
    {
        _currentUser.UserId.Returns(_account.OwnerUserId);

        await Behavior<GetAccountBalanceQuery>().Handle(new GetAccountBalanceQuery(_account.Id), Next(), CancellationToken.None);

        _handlerCalls.Should().Be(1);
    }

    [Fact]
    public async Task Another_user_gets_not_found_and_the_handler_never_runs()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        var act = () => Behavior<GetAccountBalanceQuery>().Handle(new GetAccountBalanceQuery(_account.Id), Next(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _handlerCalls.Should().Be(0);
    }

    [Fact]
    public async Task Admin_may_access_any_account()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAdmin.Returns(true);

        await Behavior<GetAccountBalanceQuery>().Handle(new GetAccountBalanceQuery(_account.Id), Next(), CancellationToken.None);

        _handlerCalls.Should().Be(1);
    }

    [Fact]
    public async Task Transfer_is_checked_against_the_source_account()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        var command = new TransferMoneyCommand(_account.Id, Guid.NewGuid(), 10m);

        var act = () => Behavior<TransferMoneyCommand>().Handle(command, Next(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Requests_without_an_account_are_not_checked()
    {
        var behavior = new AccountAccessBehavior<OpenAccountCommand, Guid>(_currentUser, _accounts);

        await behavior.Handle(new OpenAccountCommand(Guid.NewGuid(), "EUR"), Next(), CancellationToken.None);

        _handlerCalls.Should().Be(1);
        await _accounts.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    private AccountAccessBehavior<TRequest, Guid> Behavior<TRequest>()
        where TRequest : notnull =>
        new(_currentUser, _accounts);

    private RequestHandlerDelegate<Guid> Next() => _ =>
    {
        _handlerCalls++;
        return Task.FromResult(Guid.NewGuid());
    };
}
