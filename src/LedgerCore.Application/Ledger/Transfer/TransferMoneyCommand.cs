using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

/// <summary>
/// Moves money between two accounts on the platform. The source also pays the transfer fee of
/// its currency, if any, so it must cover the amount plus the fee.
/// </summary>
public sealed record TransferMoneyCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string? ReferenceId = null,
    string? Description = null) : IRequest<TransferResult>, IAccountScopedRequest, IIdempotentCommand
{
    // Only the source must belong to the caller; paying into someone else's account is the point.
    Guid IAccountScopedRequest.AccountId => SourceAccountId;
}

/// <param name="TransactionId">The transfer's ledger transaction.</param>
/// <param name="Fee">The fee charged to the source, 0 if none. It is posted as its own transaction.</param>
public sealed record TransferResult(Guid TransactionId, decimal Fee);
