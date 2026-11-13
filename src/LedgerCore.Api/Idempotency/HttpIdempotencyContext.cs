using LedgerCore.Application.Abstractions.Idempotency;

namespace LedgerCore.Api.Idempotency;

/// <summary>Reads the Idempotency-Key header of the current request.</summary>
internal sealed class HttpIdempotencyContext(IHttpContextAccessor httpContextAccessor) : IIdempotencyContext
{
    public string? Key =>
        httpContextAccessor.HttpContext?.Request.Headers[IIdempotencyContext.HeaderName].FirstOrDefault();
}
