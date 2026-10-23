using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using MediatR;

namespace LedgerCore.Application.Accounts.OpenAccount;

public sealed class OpenAccountCommandHandler(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<OpenAccountCommand, Guid>
{
    public async Task<Guid> Handle(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        var account = Account.Open(
            command.OwnerUserId,
            Currency.From(command.Currency),
            timeProvider.GetUtcNow().UtcDateTime);

        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return account.Id;
    }
}
