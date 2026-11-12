namespace LedgerCore.Application.Abstractions.Authentication;

/// <summary>The caller of the current request. Only sign-up and login run unauthenticated.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Throws if <see cref="IsAuthenticated"/> is false.</summary>
    Guid UserId { get; }

    bool IsAdmin { get; }
}
