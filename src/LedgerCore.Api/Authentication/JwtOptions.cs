using System.Text;

namespace LedgerCore.Api.Authentication;

/// <summary>
/// Bound from the "Jwt" section. Issuer and audience live in appsettings; the signing key is a
/// secret and comes from user secrets locally and the environment (Key Vault in Azure).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 needs a key of at least 256 bits.</summary>
    public const int MinSigningKeyBytes = 32;

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int LifetimeMinutes { get; init; } = 60;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && Encoding.UTF8.GetByteCount(SigningKey) >= MinSigningKeyBytes
        && LifetimeMinutes > 0;
}
