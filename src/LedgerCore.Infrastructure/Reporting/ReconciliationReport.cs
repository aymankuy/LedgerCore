namespace LedgerCore.Infrastructure.Reporting;

/// <summary>
/// The daily reconciliation: the day's activity, and three checks over the whole ledger that
/// must always hold. <see cref="IsBalanced"/> is false if any check finds a problem.
/// </summary>
public sealed record ReconciliationReport(
    DateOnly Day,
    DateTime GeneratedAtUtc,
    IReadOnlyList<CurrencyActivity> Activity,
    long UnbalancedTransactionCount,
    IReadOnlyList<UnbalancedTransaction> UnbalancedTransactionSample,
    long DriftedAccountCount,
    IReadOnlyList<DriftedAccount> DriftedAccountSample,
    IReadOnlyList<CurrencyNet> CurrencyNets)
{
    public bool IsBalanced =>
        UnbalancedTransactionCount == 0
        && DriftedAccountCount == 0
        && Activity.All(a => a.Debits == a.Credits)
        && CurrencyNets.All(c => c.Net == 0);
}

/// <summary>Money moved in one currency during the day.</summary>
public sealed record CurrencyActivity(string Currency, long Transactions, long Entries, decimal Debits, decimal Credits);

/// <summary>A transaction whose entries do not sum to zero.</summary>
public sealed record UnbalancedTransaction(Guid TransactionId, decimal Net);

/// <summary>An account whose balance as read by the API differs from the sum of its entries.</summary>
public sealed record DriftedAccount(Guid AccountId, decimal ReportedBalance, decimal LedgerBalance);

/// <summary>The sum of every account's balance in one currency; always zero.</summary>
public sealed record CurrencyNet(string Currency, decimal Net);
