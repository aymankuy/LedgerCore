namespace LedgerCore.Application.Abstractions.Authentication;

/// <summary>
/// A request that acts on one account the caller must own, unless the caller is an admin.
/// Checked once by <see cref="Behaviors.AccountAccessBehavior{TRequest, TResponse}"/>, so no
/// handler or controller can forget it.
/// </summary>
public interface IAccountScopedRequest
{
    Guid AccountId { get; }
}
