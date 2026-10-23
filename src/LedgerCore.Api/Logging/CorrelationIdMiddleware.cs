using System.Text.RegularExpressions;
using Serilog.Context;

namespace LedgerCore.Api.Logging;

/// <summary>
/// Gives every request a correlation id: the caller's X-Correlation-Id when it is safe to
/// reuse, otherwise a new one. It is attached to every log event of the request, echoed in the
/// response header and added to error responses, so a client report can be matched to logs.
/// </summary>
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();

        // Untrusted input ends up in logs, so only short, plain ids are accepted.
        var correlationId = SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        // Set just before the headers go out: the exception handler clears the response, which
        // would otherwise drop the header from exactly the responses where it matters most.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex SafeId();
}
