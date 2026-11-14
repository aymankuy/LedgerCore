using System.Globalization;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using static LedgerCore.UnitTests.Domain.TestAccounts;

namespace LedgerCore.UnitTests.Domain;

public class LedgerTransactionTests
{
    [Fact]
    public void Transfer_debits_the_source_and_credits_the_destination_for_the_same_amount()
    {
        var source = Customer();
        var destination = Customer();

        var transaction = LedgerTransaction.Transfer(source, sourceBalance: 100m, destination, amount: 40m, Now);

        transaction.Type.Should().Be(TransactionType.Transfer);
        transaction.Entries.Should().HaveCount(2);
        transaction.Entries.Should().ContainSingle(e =>
            e.AccountId == source.Id && e.Direction == EntryDirection.Debit && e.Amount == 40m);
        transaction.Entries.Should().ContainSingle(e =>
            e.AccountId == destination.Id && e.Direction == EntryDirection.Credit && e.Amount == 40m);
    }

    [Fact]
    public void Every_transaction_type_nets_to_zero()
    {
        var customer = Customer();
        var other = Customer();
        var settlement = Settlement();

        LedgerTransaction[] transactions =
        [
            LedgerTransaction.Deposit(settlement, customer, 250m, Now),
            LedgerTransaction.Withdraw(customer, 250m, settlement, 100m, Now),
            LedgerTransaction.Transfer(customer, 150m, other, 75.1234m, Now),
        ];

        transactions.Should().AllSatisfy(t => t.Net.Should().Be(0m));
    }

    [Fact]
    public void Deposit_credits_the_customer_and_debits_settlement()
    {
        var customer = Customer();
        var settlement = Settlement();

        var transaction = LedgerTransaction.Deposit(settlement, customer, 100m, Now);

        transaction.Entries.Single(e => e.AccountId == customer.Id).Direction.Should().Be(EntryDirection.Credit);
        transaction.Entries.Single(e => e.AccountId == settlement.Id).Direction.Should().Be(EntryDirection.Debit);
    }

    [Fact]
    public void Transfer_above_balance_is_rejected()
    {
        var act = () => LedgerTransaction.Transfer(Customer(), sourceBalance: 10m, Customer(), amount: 10.01m, Now);

        act.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void Withdrawal_above_balance_is_rejected()
    {
        var act = () => LedgerTransaction.Withdraw(Customer(), sourceBalance: 5m, Settlement(), amount: 6m, Now);

        act.Should().Throw<InsufficientFundsException>();
    }

    [Fact]
    public void Deposit_from_a_customer_account_is_rejected()
    {
        var act = () => LedgerTransaction.Deposit(Customer(), Customer(), 10m, Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Withdrawal_to_a_customer_account_is_rejected()
    {
        var act = () => LedgerTransaction.Withdraw(Customer(), sourceBalance: 50m, Customer(), amount: 10m, Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Transfer_involving_a_settlement_account_is_rejected()
    {
        var act = () => LedgerTransaction.Transfer(Customer(), sourceBalance: 50m, Settlement(), amount: 10m, Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Transfer_to_the_same_account_is_rejected()
    {
        var account = Customer();

        var act = () => LedgerTransaction.Transfer(account, 100m, account, 10m, Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Transfer_between_currencies_is_rejected()
    {
        var act = () => LedgerTransaction.Transfer(Customer("EUR"), 100m, Customer("TRY"), 10m, Now);

        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void Transfer_to_a_frozen_account_is_rejected()
    {
        var destination = Customer();
        destination.Freeze();

        var act = () => LedgerTransaction.Transfer(Customer(), 100m, destination, 10m, Now);

        act.Should().Throw<AccountNotActiveException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    public void Invalid_amounts_are_rejected(string amount)
    {
        var act = () => LedgerTransaction.Deposit(Settlement(), Customer(), decimal.Parse(amount, CultureInfo.InvariantCulture), Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Non_utc_timestamps_are_rejected()
    {
        var localTime = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Local);

        var act = () => LedgerTransaction.Deposit(Settlement(), Customer(), 10m, localTime);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Timestamps_are_truncated_to_milliseconds_to_match_datetime2_3()
    {
        var withSubMilliseconds = Now.AddTicks(1_234_567); // 123.4567 ms

        var transaction = LedgerTransaction.Deposit(Settlement(), Customer(), 10m, withSubMilliseconds);

        transaction.CreatedAtUtc.Should().Be(Now.AddMilliseconds(123));
        transaction.Entries.Should().AllSatisfy(e => e.CreatedAtUtc.Should().Be(transaction.CreatedAtUtc));
    }

    [Fact]
    public void Posted_event_names_the_transaction_and_each_account_once()
    {
        var source = Customer();
        var destination = Customer();

        var transfer = LedgerTransaction.Transfer(source, 100m, destination, 10m, Now);

        transfer.PostedEvent.Should().BeEquivalentTo(
            new LedgerTransactionPosted(transfer.Id, TransactionType.Transfer, [source.Id, destination.Id]));
    }
}
