using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Ledger;

/// <summary>
/// The envelope for one business event. It is only ever created through the factory methods
/// below, each of which produces a balanced set of entries, so an unbalanced transaction
/// cannot be constructed in application code. The database enforces the same rule independently.
/// </summary>
public sealed class LedgerTransaction
{
    public const int ReferenceIdMaxLength = 64;
    public const int DescriptionMaxLength = 256;

    private readonly List<LedgerEntry> _entries = [];

    public Guid Id { get; private set; }
    public TransactionType Type { get; private set; }
    public string? ReferenceId { get; private set; }
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>Sum of signed entry amounts. Always zero for a valid transaction.</summary>
    public decimal Net => _entries.Sum(e => e.SignedAmount);

    private LedgerTransaction()
    {
    }

    /// <summary>
    /// Money entering the platform: the settlement account (the outside world) is debited
    /// and the customer account is credited.
    /// </summary>
    public static LedgerTransaction Deposit(
        Account settlement, Account destination, decimal amount, DateTime utcNow,
        string? referenceId = null, string? description = null)
    {
        EnsureSameCurrency(settlement, destination);
        settlement.EnsureCanTransact();
        destination.EnsureCanTransact();

        return Create(TransactionType.Deposit, settlement.Id, destination.Id, amount, utcNow, referenceId, description);
    }

    /// <summary>
    /// Money leaving the platform: the customer account is debited and the settlement account credited.
    /// </summary>
    public static LedgerTransaction Withdraw(
        Account source, decimal sourceBalance, Account settlement, decimal amount, DateTime utcNow,
        string? referenceId = null, string? description = null)
    {
        EnsureSameCurrency(source, settlement);
        settlement.EnsureCanTransact();
        source.EnsureCanBeDebited(sourceBalance, amount);

        return Create(TransactionType.Withdrawal, source.Id, settlement.Id, amount, utcNow, referenceId, description);
    }

    public static LedgerTransaction Transfer(
        Account source, decimal sourceBalance, Account destination, decimal amount, DateTime utcNow,
        string? referenceId = null, string? description = null)
    {
        if (source.Id == destination.Id)
        {
            throw new DomainValidationException("Source and destination accounts must differ.");
        }

        EnsureSameCurrency(source, destination);
        destination.EnsureCanTransact();
        source.EnsureCanBeDebited(sourceBalance, amount);

        return Create(TransactionType.Transfer, source.Id, destination.Id, amount, utcNow, referenceId, description);
    }

    private static LedgerTransaction Create(
        TransactionType type, Guid debitAccountId, Guid creditAccountId, decimal amount, DateTime utcNow,
        string? referenceId, string? description)
    {
        Money.EnsurePositive(amount);
        var createdAt = UtcTime.Ensure(utcNow);

        var transaction = new LedgerTransaction
        {
            Id = Guid.NewGuid(),
            Type = type,
            ReferenceId = Normalize(referenceId, ReferenceIdMaxLength, nameof(referenceId)),
            Description = Normalize(description, DescriptionMaxLength, nameof(description)),
            CreatedAtUtc = createdAt,
        };

        transaction._entries.Add(new LedgerEntry(transaction.Id, debitAccountId, EntryDirection.Debit, amount, createdAt));
        transaction._entries.Add(new LedgerEntry(transaction.Id, creditAccountId, EntryDirection.Credit, amount, createdAt));

        return transaction;
    }

    private static void EnsureSameCurrency(Account a, Account b)
    {
        if (a.Currency != b.Currency)
        {
            throw new CurrencyMismatchException(a.Currency, b.Currency);
        }
    }

    private static string? Normalize(string? value, int maxLength, string name)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new DomainValidationException($"{name} must be at most {maxLength} characters.");
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
