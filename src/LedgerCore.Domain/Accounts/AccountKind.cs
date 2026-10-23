namespace LedgerCore.Domain.Accounts;

public enum AccountKind : byte
{
    /// <summary>A user's wallet.</summary>
    Customer = 1,

    /// <summary>
    /// The platform's counterpart for money entering or leaving the system.
    /// One per currency; its balance mirrors the total held in customer accounts.
    /// </summary>
    Settlement = 2,
}
