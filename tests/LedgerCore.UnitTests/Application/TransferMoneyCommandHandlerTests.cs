using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Application.Ledger.Transfer;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.UnitTests.Domain;
using NSubstitute;

namespace LedgerCore.UnitTests.Application;

public class TransferMoneyCommandHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly TransferMoneyCommandHandler _handler;

    private readonly Account _source = TestAccounts.Customer();
    private readonly Account _destination = TestAccounts.Customer();
    private readonly Account _feeRevenue = TestAccounts.FeeRevenue();

    public TransferMoneyCommandHandlerTests()
    {
        _time.GetUtcNow().Returns(new DateTimeOffset(TestAccounts.Now));
        _accounts.GetByIdForUpdateAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(_source);
        _accounts.GetByIdForUpdateAsync(_destination.Id, Arg.Any<CancellationToken>()).Returns(_destination);
        _accounts.GetFeeRevenueAsync(_source.Currency, Arg.Any<CancellationToken>()).Returns(_feeRevenue);
        _handler = Handler(FeeSchedule.None);
    }

    [Fact]
    public async Task Handle_debits_the_source_and_credits_the_destination()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(100m);

        var result = await _handler.Handle(
            new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        await _ledger.Received(1).PostAsync(
            Arg.Is<LedgerTransaction>(t =>
                t.Id == result.TransactionId &&
                t.Type == TransactionType.Transfer &&
                t.Entries.Any(e => e.AccountId == _source.Id && e.Direction == EntryDirection.Debit && e.Amount == 60m) &&
                t.Entries.Any(e => e.AccountId == _destination.Id && e.Direction == EntryDirection.Credit && e.Amount == 60m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_rejects_a_transfer_above_the_balance_and_posts_nothing()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(50m);

        var act = () => _handler.Handle(
            new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_an_unknown_destination()
    {
        var act = () => _handler.Handle(
            new TransferMoneyCommand(_source.Id, Guid.NewGuid(), 10m), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_locks_both_accounts_in_ascending_id_order_whatever_the_direction(bool sourceFirst)
    {
        var (from, to) = sourceFirst ? (_source, _destination) : (_destination, _source);
        _ledger.GetBalanceAsync(from.Id, Arg.Any<CancellationToken>()).Returns(100m);

        await _handler.Handle(new TransferMoneyCommand(from.Id, to.Id, 10m), CancellationToken.None);

        var lower = from.Id.CompareTo(to.Id) < 0 ? from.Id : to.Id;
        var higher = lower == from.Id ? to.Id : from.Id;
        Received.InOrder(() =>
        {
            _accounts.GetByIdForUpdateAsync(lower, Arg.Any<CancellationToken>());
            _accounts.GetByIdForUpdateAsync(higher, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void Validator_rejects_a_transfer_to_the_same_account()
    {
        var id = Guid.NewGuid();

        var result = new TransferMoneyCommandValidator().Validate(new TransferMoneyCommand(id, id, 10m));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_without_a_fee_rule_posts_only_the_transfer()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(100m);

        var result = await _handler.Handle(new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        result.Fee.Should().Be(0m);
        await _ledger.Received(1).PostAsync(Arg.Any<LedgerTransaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_posts_the_fee_as_its_own_transaction_to_fee_revenue()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(100m);

        var result = await Handler(FlatFeeOf(1.00m))
            .Handle(new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        result.Fee.Should().Be(1.00m);
        await _ledger.Received(1).PostAsync(
            Arg.Is<LedgerTransaction>(t =>
                t.Type == TransactionType.Fee &&
                t.ReferenceId == result.TransactionId.ToString() &&
                t.Entries.Any(e => e.AccountId == _source.Id && e.Direction == EntryDirection.Debit && e.Amount == 1.00m) &&
                t.Entries.Any(e => e.AccountId == _feeRevenue.Id && e.Direction == EntryDirection.Credit && e.Amount == 1.00m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_rejects_a_transfer_whose_amount_fits_but_not_with_the_fee_and_posts_nothing()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(60m);

        var act = () => Handler(FlatFeeOf(1.00m))
            .Handle(new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientFundsException>();
        await _ledger.DidNotReceiveWithAnyArgs().PostAsync(default!, default);
    }

    private TransferMoneyCommandHandler Handler(FeeSchedule fees) => new(_accounts, _ledger, fees, _time);

    private static FeeSchedule FlatFeeOf(decimal fee) =>
        new(new Dictionary<string, FeeRule> { ["EUR"] = new() { Type = FeeType.Flat, Amount = fee } });
}
