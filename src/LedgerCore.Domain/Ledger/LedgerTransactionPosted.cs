namespace LedgerCore.Domain.Ledger;

/// <summary>
/// Domain event: a ledger transaction was posted. It is written to the outbox in the same
/// database transaction as the entries, so it exists if and only if the transaction does.
/// </summary>
/// <param name="AccountIds">Every account whose balance the transaction changed.</param>
public sealed record LedgerTransactionPosted(Guid TransactionId, TransactionType Type, IReadOnlyList<Guid> AccountIds);
