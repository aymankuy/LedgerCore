-- GET /api/accounts/{id}/balance on a hot customer account (a cache miss).
-- Copied from GetAccountBalanceQueryHandler; keep in step with it.
SELECT a.Id AS AccountId,
       a.Currency,
       COALESCE(SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END), 0) AS Balance
FROM dbo.Accounts AS a
LEFT JOIN dbo.LedgerEntries AS e ON e.AccountId = a.Id
WHERE a.Id = @AccountId
GROUP BY a.Id, a.Currency;
