namespace LedgerCore.Application.Abstractions.Authentication;

/// <summary>The authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsAdmin { get; }
}
