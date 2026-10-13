using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using static LedgerCore.UnitTests.Domain.TestAccounts;

namespace LedgerCore.UnitTests.Domain;

public class AccountTests
{
    [Fact]
    public void Open_creates_an_active_account_without_overdraft_by_default()
    {
        var ownerId = Guid.NewGuid();

        var account = Account.Open(ownerId, Eur, Now);

        account.Id.Should().NotBeEmpty();
        account.OwnerUserId.Should().Be(ownerId);
        account.Status.Should().Be(AccountStatus.Active);
        account.AllowsOverdraft.Should().BeFalse();
        account.IsOwnedBy(ownerId).Should().BeTrue();
    }

    [Fact]
    public void Open_rejects_an_empty_owner()
    {
        var act = () => Account.Open(Guid.Empty, Eur, Now);

        act.Should().Throw<DomainValidationException>();
    }

    [Fact]
    public void Debit_larger_than_balance_is_rejected()
    {
        var account = Customer();

        var act = () => account.EnsureCanBeDebited(currentBalance: 100m, amount: 100.0001m);

        act.Should().Throw<InsufficientFundsException>()
            .Which.Code.Should().Be("insufficient_funds");
    }

    [Fact]
    public void Debit_that_brings_balance_to_exactly_zero_is_allowed()
    {
        var account = Customer();

        var act = () => account.EnsureCanBeDebited(currentBalance: 100m, amount: 100m);

        act.Should().NotThrow();
    }

    [Fact]
    public void Overdraft_account_can_go_negative()
    {
        var account = Customer(allowsOverdraft: true);

        var act = () => account.EnsureCanBeDebited(currentBalance: 0m, amount: 50m);

        act.Should().NotThrow();
    }

    [Fact]
    public void Frozen_account_cannot_be_debited_even_with_enough_balance()
    {
        var account = Customer();
        account.Freeze();

        var act = () => account.EnsureCanBeDebited(currentBalance: 1_000m, amount: 1m);

        act.Should().Throw<AccountNotActiveException>();
    }

    [Fact]
    public void Frozen_account_can_be_unfrozen()
    {
        var account = Customer();
        account.Freeze();

        account.Unfreeze();

        account.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public void Closed_account_cannot_be_frozen()
    {
        var account = Customer();
        account.Close();

        var act = () => account.Freeze();

        act.Should().Throw<InvalidAccountStateTransitionException>();
    }
}
