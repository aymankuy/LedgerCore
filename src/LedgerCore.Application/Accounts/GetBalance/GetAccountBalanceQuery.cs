using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Caching;
using MediatR;

namespace LedgerCore.Application.Accounts.GetBalance;

public sealed record GetAccountBalanceQuery(Guid AccountId)
    : IRequest<AccountBalanceResponse>, IAccountScopedRequest, ICacheableQuery
{
    public string CacheKey => CacheKeys.AccountBalance(AccountId);

    // The Worker removes the entry within about a second of a posting; this only bounds how long
    // a missed removal can serve a stale balance.
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(1);
}

public sealed record AccountBalanceResponse(Guid AccountId, string Currency, decimal Balance);
