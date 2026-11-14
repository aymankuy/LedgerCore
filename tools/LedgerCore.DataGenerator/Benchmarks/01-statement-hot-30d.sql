-- GET /api/accounts/{id}/statement: a hot customer account, 30-day window.
-- Copied from GetAccountStatementQueryHandler; keep in step with it.
DECLARE @OpeningBalance decimal(19, 4) =
    (SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0)
     FROM dbo.LedgerEntries
     WHERE AccountId = @AccountId AND CreatedAtUtc < @FromUtc);

SELECT Id AS AccountId, Currency, @OpeningBalance AS OpeningBalance
FROM dbo.Accounts
WHERE Id = @AccountId;

SELECT e.Id AS EntryId,
       e.TransactionId,
       t.Type,
       t.ReferenceId,
       t.Description,
       s.Amount,
       @OpeningBalance + SUM(s.Amount) OVER (ORDER BY e.CreatedAtUtc, e.Id ROWS UNBOUNDED PRECEDING) AS BalanceAfter,
       e.CreatedAtUtc
FROM dbo.LedgerEntries AS e
JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
CROSS APPLY (SELECT CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END AS Amount) AS s
WHERE e.AccountId = @AccountId
  AND e.CreatedAtUtc >= @FromUtc
  AND e.CreatedAtUtc < @ToUtc
ORDER BY e.CreatedAtUtc, e.Id;
