-- The four views the Power BI report reads, one per report page. Keeping the shaping in the
-- warehouse means the report holds no business logic: a number on a page can be checked with
-- a plain SELECT.

-- Daily transaction volume. Every transaction has a credit side and a debit side of the same
-- size, so volume counts the credit entries only; counting both would double it.
CREATE VIEW dbo.vw_DailyTransactionVolume
AS
SELECT d.[Date],
       t.Name AS TransactionType,
       a.Currency,
       COUNT_BIG(*) AS Transactions,
       SUM(f.Amount) AS Volume
FROM dbo.FactLedgerEntry AS f
JOIN dbo.DimDate AS d ON d.DateKey = f.DateKey
JOIN dbo.DimTransactionType AS t ON t.TransactionTypeKey = f.TransactionTypeKey
JOIN dbo.DimAccount AS a ON a.AccountKey = f.AccountKey
WHERE f.Direction = 2
GROUP BY d.[Date], t.Name, a.Currency;
GO

-- Where every account stands today: the sum of all its entries, with the account's current
-- attributes. A band column lets the report draw the distribution without its own logic.
-- Settlement accounts hold the mirror image of customer money, so reports filter on Kind.
CREATE VIEW dbo.vw_AccountBalance
AS
SELECT c.AccountId,
       c.Currency,
       c.KindName AS Kind,
       c.StatusName AS Status,
       b.Balance,
       CASE WHEN b.Balance < 0     THEN N'1  below 0'
            WHEN b.Balance = 0     THEN N'2  0'
            WHEN b.Balance < 100   THEN N'3  under 100'
            WHEN b.Balance < 1000  THEN N'4  100 to 1,000'
            WHEN b.Balance < 10000 THEN N'5  1,000 to 10,000'
            ELSE                        N'6  10,000 and over' END AS BalanceBand
FROM (SELECT d.AccountId, SUM(f.SignedAmount) AS Balance
      FROM dbo.FactLedgerEntry AS f
      JOIN dbo.DimAccount AS d ON d.AccountKey = f.AccountKey
      GROUP BY d.AccountId) AS b
JOIN dbo.DimAccount AS c ON c.AccountId = b.AccountId AND c.IsCurrent = 1;
GO

-- Money handled per account: the sum of the amounts of all its entries, in and out.
CREATE VIEW dbo.vw_TopAccountsByVolume
AS
SELECT c.AccountId,
       c.Currency,
       c.KindName AS Kind,
       COUNT_BIG(*) AS Entries,
       SUM(f.Amount) AS Volume
FROM dbo.FactLedgerEntry AS f
JOIN dbo.DimAccount AS d ON d.AccountKey = f.AccountKey
JOIN dbo.DimAccount AS c ON c.AccountId = d.AccountId AND c.IsCurrent = 1
GROUP BY c.AccountId, c.Currency, c.KindName;
GO

-- Fee revenue per day: what was credited to the fee revenue accounts.
CREATE VIEW dbo.vw_FeeRevenueDaily
AS
SELECT d.[Date],
       a.Currency,
       SUM(f.Amount) AS FeeRevenue
FROM dbo.FactLedgerEntry AS f
JOIN dbo.DimDate AS d ON d.DateKey = f.DateKey
JOIN dbo.DimAccount AS a ON a.AccountKey = f.AccountKey
WHERE f.TransactionTypeKey = 4 AND f.Direction = 2 AND a.Kind = 3
GROUP BY d.[Date], a.Currency;
GO
