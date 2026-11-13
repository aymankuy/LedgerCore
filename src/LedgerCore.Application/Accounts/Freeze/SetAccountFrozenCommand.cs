using LedgerCore.Application.Abstractions.Idempotency;
using MediatR;

namespace LedgerCore.Application.Accounts.Freeze;

/// <summary>Freezes or unfreezes an account. A frozen account can take part in no money movement.</summary>
public sealed record SetAccountFrozenCommand(Guid AccountId, bool Frozen) : IRequest, IIdempotentCommand;
