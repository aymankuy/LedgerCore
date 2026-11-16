namespace LedgerCore.Infrastructure.Persistence.Records;

/// <summary>
/// An account's balance over every entry up to the latest refresh's watermark
/// (<see cref="BalanceSnapshotRun.ToEntryId"/>). Written only by usp_RefreshBalanceSnapshots;
/// fn_AccountBalance adds the entries after the watermark.
/// </summary>
public sealed class AccountBalanceSnapshot
{
    public Guid AccountId { get; init; }
    public decimal Balance { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
