using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Application.Common;
using MediatR;

namespace LedgerCore.Application.Behaviors;

/// <summary>
/// Runs every <see cref="IIdempotentCommand"/> at most once per Idempotency-Key (spec rule 6).
/// The key is claimed in the same transaction as the command's own writes: a parallel duplicate
/// waits on the key's row until the first request commits, then gets its stored response.
/// If the first request fails, its claim rolls back with everything else, and the key is free.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyContext idempotency,
    IIdempotencyStore store,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand)
        {
            return await next();
        }

        var key = ScopedKey(RequireKey());
        var requestHash = Hash(request);

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var stored = await store.TryClaimAsync(key, requestHash, cancellationToken);
            if (stored is not null)
            {
                return stored.RequestHash == requestHash
                    ? JsonSerializer.Deserialize<TResponse>(stored.ResponseJson!)!
                    : throw new ConflictException($"This {IIdempotencyContext.HeaderName} was already used for a different request.");
            }

            var response = await next();
            await store.SaveResponseAsync(key, JsonSerializer.Serialize(response), cancellationToken);
            return response;
        }, cancellationToken);
    }

    private string RequireKey()
    {
        var key = idempotency.Key;
        if (string.IsNullOrWhiteSpace(key) || key.Length > IIdempotencyContext.MaxKeyLength)
        {
            throw new ValidationException([
                new ValidationFailure(IIdempotencyContext.HeaderName, $"The {IIdempotencyContext.HeaderName} header is required and must be 1 to {IIdempotencyContext.MaxKeyLength} characters."),
            ]);
        }

        return key;
    }

    // Keys are per user, so two users who happen to pick the same key never see each other's
    // responses. Sign-up has no user yet, so those keys share one anonymous scope.
    private string ScopedKey(string key) =>
        currentUser.IsAuthenticated ? $"{currentUser.UserId:N}:{key}" : $"anonymous:{key}";

    // The command type is part of the hash, so reusing a key for another kind of command is a
    // different request too, even if the fields happen to match.
    private static string Hash(TRequest request)
    {
        var payload = $"{typeof(TRequest).FullName}\n{JsonSerializer.Serialize(request)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
