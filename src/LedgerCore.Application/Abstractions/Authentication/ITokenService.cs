using LedgerCore.Domain.Users;

namespace LedgerCore.Application.Abstractions.Authentication;

/// <summary>Issues access tokens. The format (JWT) and signing are an API concern.</summary>
public interface ITokenService
{
    AccessToken Issue(User user);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);
