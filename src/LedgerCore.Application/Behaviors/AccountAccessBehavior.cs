using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Behaviors;

/// <summary>
/// Enforces "a user may only access their own accounts; an admin may access all" for every
/// <see cref="IAccountScopedRequest"/>. Runs after validation, so the account id is well formed.
/// </summary>
public sealed class AccountAccessBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    IAccountRepository accounts) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IAccountScopedRequest scoped && !currentUser.IsAdmin)
        {
            var account = await accounts.GetByIdAsync(scoped.AccountId, cancellationToken);

            // Someone else's account is reported exactly like a missing one, so account ids
            // cannot be probed to learn which ones exist.
            if (account is null || !account.IsOwnedBy(currentUser.UserId))
            {
                throw new NotFoundException(nameof(Account), scoped.AccountId);
            }
        }

        return await next();
    }
}
