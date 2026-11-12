using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class WithdrawCommandHandlerTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly WithdrawCommandHandler _handler;

    private readonly Account _customer = TestAccounts.Customer();
    private readonly Account _settlement = TestAccounts.Settlement();

    public WithdrawCommandHandlerTests()
    {
        _time.GetUtcNow().Returns(new DateTimeOffset(TestAccounts.Now));
        _accounts.GetByIdForUpdateAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _accounts.GetSettlementAsync(_customer.Currency, Arg.Any<CancellationToken>()).Returns(_settlement);
        _handler = new WithdrawCommandHandler(_unitOfWork, _accounts, _ledger, _time);

        // Runs the work directly, as a real transaction would before committing.
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<Guid>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<Guid>>>()());
    }

    [Fact]
    public async Task Handle_posts_a_withdrawal_to_the_settlement_account()
    {
        _ledger.GetBalanceAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(100m);

        var id = await _handler.Handle(new WithdrawCommand(_customer.Id, 40m), CancellationToken.None);

        await _ledger.Received(1).PostAsync(
            Arg.Is<LedgerTransaction>(t =>
                t.Id == id &&
                t.Type == TransactionType.Withdrawal &&
                t.Entries.Any(e => e.AccountId == _customer.Id && e.Direction == EntryDirection.Debit && e.Amount == 40m) &&
                t.Entries.Any(e => e.AccountId == _settlement.Id && e.Direction == EntryDirection.Credit && e.Amount == 40m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_rejects_a_withdrawal_above_the_balance_and_posts_nothing()
    {
        _ledger.GetBalanceAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(30m);

        var act = () => _handler.Handle(new WithdrawCommand(_customer.Id, 40m), CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    [Fact]
    public async Task Handle_locks_the_account_inside_one_transaction()
    {
        _ledger.GetBalanceAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(100m);

        await _handler.Handle(new WithdrawCommand(_customer.Id, 40m), CancellationToken.None);

        await _unitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task<Guid>>>(), Arg.Any<CancellationToken>());
        await _accounts.Received(1).GetByIdForUpdateAsync(_customer.Id, Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_an_unknown_account()
    {
        var act = () => _handler.Handle(new WithdrawCommand(Guid.NewGuid(), 10m), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.00001)]
    public void Validator_rejects_invalid_amounts(double amount)
    {
        var result = new WithdrawCommandValidator().Validate(new WithdrawCommand(Guid.NewGuid(), (decimal)amount));

        result.IsValid.Should().BeFalse();
    }
}
