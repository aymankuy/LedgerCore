namespace LedgerCore.Application.Abstractions.Idempotency;

/// <summary>The Idempotency-Key the client sent with the current request, if any.</summary>
public interface IIdempotencyContext
{
    const string HeaderName = "Idempotency-Key";
    const int MaxKeyLength = 64;

    string? Key { get; }
}
