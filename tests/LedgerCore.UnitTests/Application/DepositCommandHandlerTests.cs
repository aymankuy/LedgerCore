using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class DepositCommandHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly DepositCommandHandler _handler;

    public DepositCommandHandlerTests()
    {
        _time.GetUtcNow().Returns(new DateTimeOffset(TestAccounts.Now));
        _handler = new DepositCommandHandler(_accounts, _ledger, _time);
    }

    [Fact]
    public async Task Handle_posts_a_deposit_from_the_settlement_account()
    {
        var customer = TestAccounts.Customer();
        var settlement = TestAccounts.Settlement();
        _accounts.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        _accounts.GetSettlementAsync(customer.Currency, Arg.Any<CancellationToken>()).Returns(settlement);

        var id = await _handler.Handle(new DepositCommand(customer.Id, 25m), CancellationToken.None);

        await _ledger.Received(1).PostAsync(
            Arg.Is<LedgerTransaction>(t =>
                t.Id == id &&
                t.Type == TransactionType.Deposit &&
                t.Entries.Any(e => e.AccountId == settlement.Id && e.Direction == EntryDirection.Debit && e.Amount == 25m) &&
                t.Entries.Any(e => e.AccountId == customer.Id && e.Direction == EntryDirection.Credit && e.Amount == 25m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_throws_not_found_for_an_unknown_account()
    {
        _accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Account?)null);

        var act = () => _handler.Handle(new DepositCommand(Guid.NewGuid(), 25m), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.00001)]
    public void Validator_rejects_invalid_amounts(double amount)
    {
        var result = new DepositCommandValidator().Validate(new DepositCommand(Guid.NewGuid(), (decimal)amount));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_a_valid_deposit()
    {
        var result = new DepositCommandValidator().Validate(new DepositCommand(Guid.NewGuid(), 10.5m, "ref-1"));

        result.IsValid.Should().BeTrue();
    }
}
