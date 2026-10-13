namespace LedgerCore.Domain.Ledger;

/// <summary>
/// Sign convention for wallet accounts: a Credit increases the account's balance and a Debit decreases it.
/// Balance = SUM(credits) - SUM(debits).
/// </summary>
public enum EntryDirection : byte
{
    Debit = 1,
    Credit = 2,
}
