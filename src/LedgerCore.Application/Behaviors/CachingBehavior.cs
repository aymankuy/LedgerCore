using System.Text.Json;
using LedgerCore.Application.Abstractions.Caching;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LedgerCore.Application.Behaviors;

/// <summary>
/// Serves an <see cref="ICacheableQuery"/> from the cache, or runs it and stores the response.
/// It runs after the access check, so a cached balance is only ever returned to a caller allowed
/// to read that account. The cache is an optimisation: if it fails, the query runs as usual.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    ICache cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery query)
        {
            return await next(cancellationToken);
        }

        try
        {
            var cached = await cache.GetAsync(query.CacheKey, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<TResponse>(cached)!;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache read failed for {CacheKey}; querying the database", query.CacheKey);
        }

        var response = await next(cancellationToken);

        try
        {
            await cache.SetAsync(query.CacheKey, JsonSerializer.Serialize(response), query.CacheDuration, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache write failed for {CacheKey}", query.CacheKey);
        }

        return response;
    }
}
