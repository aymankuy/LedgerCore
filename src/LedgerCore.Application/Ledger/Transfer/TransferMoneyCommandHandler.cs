using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

public sealed class TransferMoneyCommandHandler(
    IUnitOfWork unitOfWork,
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<TransferMoneyCommand, Guid>
{
    public Task<Guid> Handle(TransferMoneyCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Only the source is balance-checked, so only the source is locked for now.
            // Deterministic lock ordering of both accounts follows in the next step.
            var source = await accounts.GetByIdForUpdateAsync(command.SourceAccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(Account), command.SourceAccountId);

            var destination = await accounts.GetByIdAsync(command.DestinationAccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(Account), command.DestinationAccountId);

            var sourceBalance = await ledger.GetBalanceAsync(source.Id, cancellationToken);

            var transaction = LedgerTransaction.Transfer(
                source,
                sourceBalance,
                destination,
                command.Amount,
                timeProvider.GetUtcNow().UtcDateTime,
                command.ReferenceId,
                command.Description);

            await ledger.PostAsync(transaction, cancellationToken);

            return transaction.Id;
        }, cancellationToken);
}
