using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

/// <summary>Moves money between two accounts on the platform. Returns the ledger transaction id.</summary>
public sealed record TransferMoneyCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string? ReferenceId = null,
    string? Description = null) : IRequest<Guid>, IAccountScopedRequest, IIdempotentCommand
{
    // Only the source must belong to the caller; paying into someone else's account is the point.
    Guid IAccountScopedRequest.AccountId => SourceAccountId;
}
