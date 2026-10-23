using Microsoft.IdentityModel.JsonWebTokens;

namespace LedgerCore.Api.Authentication;

/// <summary>
/// Claim names as they appear in the token. Inbound claim mapping is switched off, so the same
/// short names are used when issuing a token and when reading it back from <c>User</c>.
/// </summary>
public static class AuthClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string Role = "role";
}
