using MediatR;

namespace LedgerCore.Application.Ledger.Deposit;

/// <summary>Credits money from outside the platform to an account. Returns the ledger transaction id.</summary>
public sealed record DepositCommand(
    Guid AccountId,
    decimal Amount,
    string? ReferenceId = null,
    string? Description = null) : IRequest<Guid>;
