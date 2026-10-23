using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

public sealed class TransferMoneyCommandHandler(
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<TransferMoneyCommand, Guid>
{
    public async Task<Guid> Handle(TransferMoneyCommand command, CancellationToken cancellationToken)
    {
        var source = await accounts.GetByIdAsync(command.SourceAccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), command.SourceAccountId);

        var destination = await accounts.GetByIdAsync(command.DestinationAccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), command.DestinationAccountId);

        // Deliberately naive for Phase 1, like withdrawals: no lock, no isolation level and no
        // lock ordering yet. Phase 2 adds all three and the tests that prove they are needed.
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
    }
}
