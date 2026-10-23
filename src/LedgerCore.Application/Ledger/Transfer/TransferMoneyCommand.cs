using MediatR;

namespace LedgerCore.Application.Ledger.Transfer;

/// <summary>Moves money between two accounts on the platform. Returns the ledger transaction id.</summary>
public sealed record TransferMoneyCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string? ReferenceId = null,
    string? Description = null) : IRequest<Guid>;
