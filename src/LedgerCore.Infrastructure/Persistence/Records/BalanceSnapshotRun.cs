namespace LedgerCore.Infrastructure.Persistence.Records;

/// <summary>
/// One run of usp_RefreshBalanceSnapshots. It folded the entries with
/// FromEntryId &lt; Id &lt;= ToEntryId into the snapshots. The latest run's ToEntryId is the
/// watermark every balance read starts from.
/// </summary>
public sealed class BalanceSnapshotRun
{
    public int Id { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime FinishedAtUtc { get; init; }
    public long FromEntryId { get; init; }
    public long ToEntryId { get; init; }
    public int AccountsUpdated { get; init; }
}
