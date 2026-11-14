namespace LedgerCore.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How long the Worker waits between polls when the outbox is empty.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Messages claimed per batch, all in one short transaction.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed attempts a message is left alone and logged as an error.</summary>
    public int MaxAttempts { get; set; } = 5;
}
