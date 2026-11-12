namespace LedgerCore.Application.Abstractions.Idempotency;

/// <summary>
/// Marks a write command that must carry an Idempotency-Key (spec rule 6). Sending the same key
/// again returns the first response instead of running the command a second time.
/// </summary>
public interface IIdempotentCommand;
