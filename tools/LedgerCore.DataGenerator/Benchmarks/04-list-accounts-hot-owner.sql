-- GET /api/accounts for the owner of the hot account.
-- Copied from ListMyAccountsQueryHandler; keep in step with it.
SELECT a.Id AS AccountId,
       a.Currency,
       a.Status,
       COALESCE(SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END), 0) AS Balance,
       a.CreatedAtUtc
FROM dbo.Accounts AS a
LEFT JOIN dbo.LedgerEntries AS e ON e.AccountId = a.Id
WHERE a.OwnerUserId = @OwnerUserId AND a.Kind = 1
GROUP BY a.Id, a.Currency, a.Status, a.CreatedAtUtc
ORDER BY a.CreatedAtUtc, a.Id;
