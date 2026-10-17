using LedgerCore.Domain.Ledger;

namespace LedgerCore.Application.Abstractions.Persistence;

/// <summary>
/// Write side of the ledger. Transactions are posted as a whole through a stored procedure,
/// never through change tracking, so the database can verify they net to zero (ADR 0001).
/// </summary>
public interface ILedgerRepository
{
    /// <summary>Balance derived from ledger entries: sum of credits minus sum of debits.</summary>
    Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken);

    Task PostAsync(LedgerTransaction transaction, CancellationToken cancellationToken);
}
