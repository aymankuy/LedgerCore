using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Domain.Users;

namespace LedgerCore.Api.Authentication;

/// <summary>Reads the caller from the validated JWT on the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        Guid.TryParse(Principal.FindFirst(AuthClaims.UserId)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("The request has no authenticated user.");

    public bool IsAdmin => Principal.IsInRole(nameof(UserRole.Admin));

    private System.Security.Claims.ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("There is no current HTTP request.");
}
