using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace LedgerCore.Infrastructure.Authentication;

/// <summary>
/// ASP.NET Core Identity's hasher on its own, without the rest of Identity: PBKDF2 with
/// HMAC-SHA512, a random salt per password, and the iteration count stored in the hash itself.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    // The Identity hasher accepts a user for custom implementations; the default one ignores it.
    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(null!, passwordHash, password) != PasswordVerificationResult.Failed;
}
