namespace LedgerCore.Infrastructure.Persistence.Records;

/// <summary>
/// An event written in the same database transaction as the ledger change it describes,
/// then published later by the Worker. Unprocessed rows have ProcessedAtUtc = NULL.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public required string Type { get; init; }
    public required string Payload { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
}