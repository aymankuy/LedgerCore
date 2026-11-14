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

    /// <summary>
    /// The platform's income from transfer fees. One per currency; it is only ever credited,
    /// so its balance is the fee revenue to date.
    /// </summary>
    FeeRevenue = 3,
}
