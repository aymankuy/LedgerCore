using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
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

    public TransferMoneyCommandHandlerTests()
    {
        _time.GetUtcNow().Returns(new DateTimeOffset(TestAccounts.Now));
        _accounts.GetByIdAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(_source);
        _accounts.GetByIdAsync(_destination.Id, Arg.Any<CancellationToken>()).Returns(_destination);
        _handler = new TransferMoneyCommandHandler(_accounts, _ledger, _time);
    }

    [Fact]
    public async Task Handle_debits_the_source_and_credits_the_destination()
    {
        _ledger.GetBalanceAsync(_source.Id, Arg.Any<CancellationToken>()).Returns(100m);

        var id = await _handler.Handle(
            new TransferMoneyCommand(_source.Id, _destination.Id, 60m), CancellationToken.None);

        await _ledger.Received(1).PostAsync(
            Arg.Is<LedgerTransaction>(t =>
                t.Id == id &&
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

    [Fact]
    public void Validator_rejects_a_transfer_to_the_same_account()
    {
        var id = Guid.NewGuid();

        var result = new TransferMoneyCommandValidator().Validate(new TransferMoneyCommand(id, id, 10m));

        result.IsValid.Should().BeFalse();
    }
}
