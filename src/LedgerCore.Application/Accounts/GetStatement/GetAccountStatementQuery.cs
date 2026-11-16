using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Accounts.GetStatement;

/// <summary>
/// One page of an account's entries in the half-open window [FromUtc, ToUtc), with running
/// balances. Pass the previous page's <see cref="AccountStatementResponse.NextCursor"/> as
/// <paramref name="Cursor"/>, with the same window, to get the page after it.
/// </summary>
public sealed record GetAccountStatementQuery(
    Guid AccountId,
    DateTime FromUtc,
    DateTime ToUtc,
    int PageSize = GetAccountStatementQuery.DefaultPageSize,
    string? Cursor = null)
    : IRequest<AccountStatementResponse>, IAccountScopedRequest
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 1_000;
}

/// <summary>
/// <see cref="OpeningBalance"/> is the balance just before the page's first line and
/// <see cref="ClosingBalance"/> the balance after its last line. For the first page the
/// opening balance is the balance at FromUtc. <see cref="NextCursor"/> is null on the last page.
/// </summary>
public sealed record AccountStatementResponse(
    Guid AccountId,
    string Currency,
    DateTime FromUtc,
    DateTime ToUtc,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<StatementLine> Lines,
    string? NextCursor);

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
