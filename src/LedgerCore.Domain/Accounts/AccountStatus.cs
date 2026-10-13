namespace LedgerCore.Domain.Accounts;

public enum AccountStatus : byte
{
    Active = 1,
    Frozen = 2,
    Closed = 3,
}
