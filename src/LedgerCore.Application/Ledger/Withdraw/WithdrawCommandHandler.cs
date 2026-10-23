using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Withdraw;

public sealed class WithdrawCommandHandler(
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<WithdrawCommand, Guid>
{
    public async Task<Guid> Handle(WithdrawCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), command.AccountId);

        var settlement = await accounts.GetSettlementAsync(account.Currency, cancellationToken)
            ?? throw new InvalidOperationException($"No settlement account exists for {account.Currency}.");

        // Deliberately naive for Phase 1: nothing stops a concurrent withdrawal from spending
        // the same balance between this read and the post below. Phase 2 closes that gap.
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
    }
}
