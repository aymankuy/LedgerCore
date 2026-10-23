using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Accounts.ListAccounts;

/// <summary>The caller's own customer accounts with their current balances.</summary>
public sealed record ListMyAccountsQuery : IRequest<IReadOnlyList<AccountSummary>>;

public sealed record AccountSummary(
    Guid AccountId,
    string Currency,
    AccountStatus Status,
    decimal Balance,
    DateTime CreatedAtUtc);
