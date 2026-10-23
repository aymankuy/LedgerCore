using MediatR;

namespace LedgerCore.Application.Ledger.Withdraw;

/// <summary>Sends money from an account out of the platform. Returns the ledger transaction id.</summary>
public sealed record WithdrawCommand(
    Guid AccountId,
    decimal Amount,
    string? ReferenceId = null,
    string? Description = null) : IRequest<Guid>;
