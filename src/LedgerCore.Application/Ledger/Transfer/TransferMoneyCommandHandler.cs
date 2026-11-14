using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

/// <summary>Runs inside the command's transaction, opened by TransactionBehavior.</summary>
public sealed class TransferMoneyCommandHandler(
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<TransferMoneyCommand, Guid>
{
    public async Task<Guid> Handle(TransferMoneyCommand command, CancellationToken cancellationToken)
    {
        // Both accounts are locked: the source because its balance is checked, the destination
        // so its status cannot change while money moves into it. They are always locked in
        // ascending id order. If A -> B locked A first and B -> A locked B first, each would
        // wait for the other forever (ADR 0002).
        var (first, second) = command.SourceAccountId.CompareTo(command.DestinationAccountId) < 0
            ? (command.SourceAccountId, command.DestinationAccountId)
            : (command.DestinationAccountId, command.SourceAccountId);

        var firstAccount = await accounts.GetByIdForUpdateAsync(first, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), first);

        var secondAccount = await accounts.GetByIdForUpdateAsync(second, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), second);

        var (source, destination) = first == command.SourceAccountId
            ? (firstAccount, secondAccount)
            : (secondAccount, firstAccount);

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
