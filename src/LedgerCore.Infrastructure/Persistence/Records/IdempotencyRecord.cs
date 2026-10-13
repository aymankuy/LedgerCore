namespace LedgerCore.Infrastructure.Persistence.Records;

/// <summary>
/// The stored outcome of a write request, keyed by its Idempotency-Key header.
/// A persistence concern rather than a business concept, so it lives here and not in Domain.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 128;

    public required string Key { get; init; }

    /// <summary>SHA-256 of the request payload, hex encoded (64 chars).</summary>
    public required string RequestHash { get; init; }

    public string? ResponseBody { get; set; }
    public int? StatusCode { get; set; }
    public DateTime CreatedAtUtc { get; init; }
}
