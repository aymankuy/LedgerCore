using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

/// <summary>Runs inside the command's transaction, opened by TransactionBehavior.</summary>
public sealed class TransferMoneyCommandHandler(
    IAccountRepository accounts,
    ILedgerRepository ledger,
    FeeSchedule fees,
    TimeProvider timeProvider) : IRequestHandler<TransferMoneyCommand, TransferResult>
{
    public async Task<TransferResult> Handle(TransferMoneyCommand command, CancellationToken cancellationToken)
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
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var transfer = LedgerTransaction.Transfer(
            source,
            sourceBalance,
            destination,
            command.Amount,
            now,
            command.ReferenceId,
            command.Description);

        // Both transactions are built before either is posted, so a balance that covers the
        // amount but not the fee rejects the whole transfer with insufficient funds.
        var fee = fees.For(source.Currency).Calculate(command.Amount);
        var feeTransaction = fee > 0m
            ? LedgerTransaction.Fee(
                source, sourceBalance - command.Amount, await GetFeeRevenueAsync(source, cancellationToken), fee, now, transfer.Id)
            : null;

        await ledger.PostAsync(transfer, cancellationToken);
        if (feeTransaction is not null)
        {
            await ledger.PostAsync(feeTransaction, cancellationToken);
        }

        return new TransferResult(transfer.Id, fee);
    }

    // Not locked: the fee revenue account is only ever credited, never balance-checked.
    private async Task<Account> GetFeeRevenueAsync(Account source, CancellationToken cancellationToken) =>
        await accounts.GetFeeRevenueAsync(source.Currency, cancellationToken)
        ?? throw new InvalidOperationException($"No fee revenue account exists for {source.Currency}.");
}
