namespace LedgerCore.Domain.Ledger;

public enum TransactionType : byte
{
    Deposit = 1,
    Withdrawal = 2,
    Transfer = 3,
    Fee = 4,
    Reversal = 5,
}
