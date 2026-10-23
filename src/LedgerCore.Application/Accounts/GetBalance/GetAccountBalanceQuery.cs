using MediatR;

namespace LedgerCore.Application.Accounts.GetBalance;

public sealed record GetAccountBalanceQuery(Guid AccountId) : IRequest<AccountBalanceResponse>;

public sealed record AccountBalanceResponse(Guid AccountId, string Currency, decimal Balance);
