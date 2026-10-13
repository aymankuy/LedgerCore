using LedgerCore.Domain.Common;

namespace LedgerCore.Domain.Users;

public sealed class User
{
    public const int EmailMaxLength = 256;

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private User()
    {
    }

    /// <summary>The password must already be hashed; the domain never sees plain text.</summary>
    public static User Register(string email, string passwordHash, UserRole role, DateTime utcNow)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalizedEmail) || normalizedEmail.Length > EmailMaxLength || !normalizedEmail.Contains('@'))
        {
            throw new DomainValidationException("A valid email address is required.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainValidationException("A password hash is required.");
        }

        return new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAtUtc = UtcTime.Ensure(utcNow),
        };
    }
}
