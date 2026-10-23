using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Accounts;

/// <summary>
/// A wallet that holds money in a single currency. Deliberately has no Balance property:
/// the balance is always derived from ledger entries, so it can never drift from the ledger.
/// </summary>
public sealed class Account
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Currency Currency { get; private set; }
    public AccountKind Kind { get; private set; }
    public AccountStatus Status { get; private set; }
    public bool AllowsOverdraft { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Account()
    {
    }

    public static Account Open(Guid ownerUserId, Currency currency, DateTime utcNow, bool allowsOverdraft = false)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new DomainValidationException("An account must have an owner.");
        }

        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Currency = currency,
            Kind = AccountKind.Customer,
            Status = AccountStatus.Active,
            AllowsOverdraft = allowsOverdraft,
            CreatedAtUtc = UtcTime.Ensure(utcNow),
        };
    }

    /// <summary>
    /// Opens the settlement account for a currency. It is the debit side of every deposit, so its
    /// balance is expected to go negative and overdraft is always allowed.
    /// </summary>
    public static Account OpenSettlement(Guid systemUserId, Currency currency, DateTime utcNow)
    {
        var account = Open(systemUserId, currency, utcNow, allowsOverdraft: true);
        account.Kind = AccountKind.Settlement;
        return account;
    }

    public bool IsOwnedBy(Guid userId) => OwnerUserId == userId;

    public void EnsureCanTransact()
    {
        if (Status != AccountStatus.Active)
        {
            throw new AccountNotActiveException(Id, Status);
        }
    }

    /// <summary>
    /// Guards rule 5: the balance may only go negative on accounts flagged AllowsOverdraft.
    /// <paramref name="currentBalance"/> must be read inside the same transaction that writes the debit.
    /// </summary>
    public void EnsureCanBeDebited(decimal currentBalance, decimal amount)
    {
        EnsureCanTransact();
        Money.EnsurePositive(amount);

        if (!AllowsOverdraft && currentBalance - amount < 0)
        {
            throw new InsufficientFundsException(Id, currentBalance, amount);
        }
    }

    public void Freeze() => TransitionTo(AccountStatus.Frozen, allowedFrom: AccountStatus.Active);

    public void Unfreeze() => TransitionTo(AccountStatus.Active, allowedFrom: AccountStatus.Frozen);

    /// <summary>Closing is terminal. Callers must ensure the balance is zero first.</summary>
    public void Close()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new InvalidAccountStateTransitionException(Id, Status, AccountStatus.Closed);
        }

        Status = AccountStatus.Closed;
    }

    private void TransitionTo(AccountStatus target, AccountStatus allowedFrom)
    {
        if (Status != allowedFrom)
        {
            throw new InvalidAccountStateTransitionException(Id, Status, target);
        }

        Status = target;
    }
}
