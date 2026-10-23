using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Accounts.GetStatement;

/// <summary>Entries of one account in the half-open window [FromUtc, ToUtc), with running balances.</summary>
public sealed record GetAccountStatementQuery(Guid AccountId, DateTime FromUtc, DateTime ToUtc)
    : IRequest<AccountStatementResponse>, IAccountScopedRequest;

public sealed record AccountStatementResponse(
    Guid AccountId,
    string Currency,
    DateTime FromUtc,
    DateTime ToUtc,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<StatementLine> Lines);

/// <summary>One ledger entry. <see cref="Amount"/> is signed: credits positive, debits negative.</summary>
public sealed record StatementLine(
    long EntryId,
    Guid TransactionId,
    TransactionType Type,
    string? ReferenceId,
    string? Description,
    decimal Amount,
    decimal BalanceAfter,
    DateTime CreatedAtUtc);
