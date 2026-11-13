using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using MediatR;

namespace LedgerCore.Application.Accounts.Freeze;

public sealed class SetAccountFrozenCommandHandler(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork) : IRequestHandler<SetAccountFrozenCommand>
{
    public async Task Handle(SetAccountFrozenCommand command, CancellationToken cancellationToken)
    {
        // Optimistic on purpose: status changes are rare, so no row lock is taken. The UPDATE only
        // matches if Accounts.RowVersion is still the value read here. If another request changed
        // the account in between, SaveChanges fails with a 409 instead of overwriting that change.
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), command.AccountId);

        if (command.Frozen)
        {
            account.Freeze();
        }
        else
        {
            account.Unfreeze();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
