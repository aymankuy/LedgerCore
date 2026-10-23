using MediatR;

namespace LedgerCore.Application.Accounts.OpenAccount;

/// <summary>Opens a new wallet for a user. Returns the new account id.</summary>
public sealed record OpenAccountCommand(Guid OwnerUserId, string Currency) : IRequest<Guid>;
