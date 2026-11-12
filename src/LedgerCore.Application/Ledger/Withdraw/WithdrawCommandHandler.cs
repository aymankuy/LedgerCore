using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Withdraw;

public sealed class WithdrawCommandHandler(
    IUnitOfWork unitOfWork,
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<WithdrawCommand, Guid>
{
    public Task<Guid> Handle(WithdrawCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // The lock is held until commit, so a parallel withdrawal from this account waits here
            // and then reads a balance that already includes this one (ADR 0002).
            var account = await accounts.GetByIdForUpdateAsync(command.AccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(Account), command.AccountId);

            // Not locked: the settlement account is never balance-checked, and locking it would
            // serialize every withdrawal in this currency.
            var settlement = await accounts.GetSettlementAsync(account.Currency, cancellationToken)
                ?? throw new InvalidOperationException($"No settlement account exists for {account.Currency}.");

            var balance = await ledger.GetBalanceAsync(account.Id, cancellationToken);

            var transaction = LedgerTransaction.Withdraw(
                account,
                balance,
                settlement,
                command.Amount,
                timeProvider.GetUtcNow().UtcDateTime,
                command.ReferenceId,
                command.Description);

            await ledger.PostAsync(transaction, cancellationToken);

            return transaction.Id;
        }, cancellationToken);
}
