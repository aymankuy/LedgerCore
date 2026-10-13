using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Ledger;

/// <summary>
/// One leg of a ledger transaction. Immutable once created: there are no setters and no
/// mutating methods, mirroring the append-only rule enforced in the database.
/// </summary>
public sealed class LedgerEntry
{
    /// <summary>Assigned by the database (IDENTITY); 0 until persisted.</summary>
    public long Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Amount with the balance sign applied: credits positive, debits negative.</summary>
    public decimal SignedAmount => Direction == EntryDirection.Credit ? Amount : -Amount;

    private LedgerEntry()
    {
    }

    internal LedgerEntry(Guid transactionId, Guid accountId, EntryDirection direction, decimal amount, DateTime createdAtUtc)
    {
        TransactionId = transactionId;
        AccountId = accountId;
        Direction = direction;
        Amount = Money.EnsurePositive(amount);
        CreatedAtUtc = createdAtUtc;
    }
}
