-- GET /api/accounts/{id}/balance on a hot customer account (a cache miss).
-- Copied from GetAccountBalanceQueryHandler; keep in step with it.
SELECT a.Id AS AccountId, a.Currency, b.Balance
FROM dbo.Accounts AS a
CROSS APPLY dbo.fn_AccountBalance(a.Id) AS b
WHERE a.Id = @AccountId;
