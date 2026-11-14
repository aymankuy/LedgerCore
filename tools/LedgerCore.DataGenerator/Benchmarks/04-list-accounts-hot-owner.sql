-- GET /api/accounts for the owner of the hot account.
-- Copied from ListMyAccountsQueryHandler; keep in step with it.
SELECT a.Id AS AccountId, a.Currency, a.Status, b.Balance, a.CreatedAtUtc
FROM dbo.Accounts AS a
CROSS APPLY dbo.fn_AccountBalance(a.Id) AS b
WHERE a.OwnerUserId = @OwnerUserId AND a.Kind = 1
ORDER BY a.CreatedAtUtc, a.Id;
