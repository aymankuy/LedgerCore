-- The same balance query on the EUR settlement account, the busiest account of all.
-- Copied from GetAccountBalanceQueryHandler; keep in step with it.
SELECT a.Id AS AccountId, a.Currency, b.Balance
FROM dbo.Accounts AS a
CROSS APPLY dbo.fn_AccountBalance(a.Id) AS b
WHERE a.Id = @SettlementAccountId;
