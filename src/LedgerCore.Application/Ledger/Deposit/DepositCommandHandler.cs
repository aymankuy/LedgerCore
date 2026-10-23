using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using MediatR;

namespace LedgerCore.Application.Ledger.Deposit;

public sealed class DepositCommandHandler(
    IAccountRepository accounts,
    ILedgerRepository ledger,
    TimeProvider timeProvider) : IRequestHandler<DepositCommand, Guid>
{
    public async Task<Guid> Handle(DepositCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new NotFoundException(nameof(Account), command.AccountId);

        // A missing settlement account is a deployment fault, not a client error.
        var settlement = await accounts.GetSettlementAsync(account.Currency, cancellationToken)
            ?? throw new InvalidOperationException($"No settlement account exists for {account.Currency}.");

        var transaction = LedgerTransaction.Deposit(
            settlement,
            account,
            command.Amount,
            timeProvider.GetUtcNow().UtcDateTime,
            command.ReferenceId,
            command.Description);

        // The stored procedure writes the envelope and its entries atomically (ADR 0001),
        // so there is nothing for the unit of work to save here.
        await ledger.PostAsync(transaction, cancellationToken);

        return transaction.Id;
    }
}
