using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LedgerCore.Api.RateLimiting;

/// <summary>
/// Settings for the limit on the unauthenticated auth endpoints (register and login).
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    /// <summary>Requests one client address may make within the window, successful or not.</summary>
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;

    public bool IsValid() => PermitLimit > 0 && WindowSeconds > 0;
}

internal static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";
    private const int Segments = 6;

    /// <summary>
    /// Login and register are open to anyone, so a script can try passwords at the speed of the
    /// network. This caps how fast one client address can try: the sliding window means a burst
    /// split across two windows still counts as one.
    /// </summary>
    public static IServiceCollection AddAuthRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthRateLimitOptions>()
            .Bind(configuration.GetSection(AuthRateLimitOptions.SectionName))
            .Validate(o => o.IsValid(), "RateLimiting:Auth needs a positive PermitLimit and WindowSeconds.")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(AuthPolicy, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;

                // On App Service the platform's forwarded-headers handling makes this the caller's
                // address, not the front end's. Without a connection (in-memory tests) all share one.
                var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetSlidingWindowLimiter(client, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    SegmentsPerWindow = Segments,
                    QueueLimit = 0,
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;

                // The sliding window limiter reports no retry time of its own. Room frees up one
                // segment at a time, so a client that waits one segment length has a chance.
                var options = httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                var retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : Math.Max(1, (int)Math.Ceiling((double)options.WindowSeconds / Segments));
                httpContext.Response.Headers.RetryAfter = retrySeconds.ToString(CultureInfo.InvariantCulture);

                // The same RFC 7807 shape and correlation id as every other error.
                await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Too many attempts from this address. Wait before trying again.",
                        Extensions = { ["code"] = "rate_limited" },
                    },
                });
            };
        });

        return services;
    }
}
