using LedgerCore.Domain.Accounts;

namespace LedgerCore.Infrastructure.Persistence.Records;

/// <summary>
/// One row per status an account has had, with the moment it took effect. Written only by
/// TR_Accounts_StatusHistory, in the same transaction as the change itself, so it cannot be
/// skipped by any code path. The warehouse builds its slowly changing dimension from it.
/// </summary>
public sealed class AccountStatusChange
{
    public long Id { get; init; }
    public Guid AccountId { get; init; }
    public AccountStatus Status { get; init; }
    public DateTime ChangedAtUtc { get; init; }
}
