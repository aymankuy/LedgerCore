using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LedgerCore.Application.Behaviors;

/// <summary>
/// Logs one structured line per request: its name, how long it took and how it ended. It runs
/// first, so rejected requests are logged too. The payload is never logged: sign-up carries a
/// password, and amounts and account ids are already in the ledger.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await next(cancellationToken);
            logger.LogInformation(
                "{RequestName} succeeded in {ElapsedMs:0} ms", requestName, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            // Expected outcomes such as insufficient funds land here too, so this is a warning;
            // the exception handler logs real failures as errors with the stack trace.
            logger.LogWarning(
                "{RequestName} failed with {ExceptionType} in {ElapsedMs:0} ms",
                requestName, exception.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
