namespace LedgerCore.Application.Abstractions.Idempotency;

/// <summary>
/// Stores idempotency keys. Both methods must run inside the command's transaction, so the key
/// is committed together with the business write or not at all.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims <paramref name="key"/> for this request. Returns null if the key is new. If another
    /// request holds the key but has not committed yet, waits for it. If the key already exists,
    /// returns what was stored for it.
    /// </summary>
    Task<StoredRequest?> TryClaimAsync(string key, string requestHash, CancellationToken cancellationToken);

    /// <summary>Saves the response of the request that claimed <paramref name="key"/>.</summary>
    Task SaveResponseAsync(string key, string responseJson, CancellationToken cancellationToken);
}

public sealed record StoredRequest(string RequestHash, string? ResponseJson);
