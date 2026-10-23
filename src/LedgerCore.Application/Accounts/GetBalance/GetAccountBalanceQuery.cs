using LedgerCore.Application.Abstractions.Authentication;
using MediatR;

namespace LedgerCore.Application.Accounts.GetBalance;

public sealed record GetAccountBalanceQuery(Guid AccountId) : IRequest<AccountBalanceResponse>, IAccountScopedRequest;

public sealed record AccountBalanceResponse(Guid AccountId, string Currency, decimal Balance);
