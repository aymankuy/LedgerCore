using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 5: the daily reconciliation report. It re-checks the ledger's invariants from the raw
    /// entries, independently of the trigger that enforces them on insert and of the snapshots
    /// derived from them.
    /// </summary>
    public partial class AddReconciliationReportProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetReconciliationReport
                    @DayStartUtc datetime2(3),
                    @SampleSize  int = 20
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @DayEndUtc datetime2(3) = DATEADD(day, 1, @DayStartUtc);

                    -- Each check below is one statement, so under READ_COMMITTED_SNAPSHOT it sees
                    -- one committed state of the ledger even while postings continue.

                    -- 1. The day's activity, per currency. Debits and credits must be equal.
                    SELECT a.Currency,
                           COUNT_BIG(DISTINCT e.TransactionId) AS Transactions,
                           COUNT_BIG(*) AS Entries,
                           SUM(CASE e.Direction WHEN 1 THEN e.Amount ELSE 0 END) AS Debits,
                           SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE 0 END) AS Credits
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.Accounts AS a ON a.Id = e.AccountId
                    WHERE e.CreatedAtUtc >= @DayStartUtc AND e.CreatedAtUtc < @DayEndUtc
                    GROUP BY a.Currency
                    ORDER BY a.Currency;

                    -- 2. Every transaction ever posted nets to zero. The trigger enforces this
                    -- on insert; this re-checks the stored rows. Reads the narrow TransactionId index.
                    SELECT COUNT_BIG(*) OVER () AS Total, u.TransactionId, u.Net
                    FROM (SELECT TransactionId, SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END) AS Net
                          FROM dbo.LedgerEntries
                          GROUP BY TransactionId
                          HAVING SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END) <> 0) AS u
                    ORDER BY u.TransactionId
                    OFFSET 0 ROWS FETCH NEXT @SampleSize ROWS ONLY;

                    -- 3. Every account's balance as the API reads it (snapshot plus newer entries)
                    -- equals the sum of all its entries. Catches a snapshot that drifted.
                    SELECT COUNT_BIG(*) OVER () AS Total, m.AccountId, m.ReportedBalance, m.LedgerBalance
                    FROM (SELECT a.Id AS AccountId, b.Balance AS ReportedBalance, ISNULL(raw.Total, 0) AS LedgerBalance
                          FROM dbo.Accounts AS a
                          CROSS APPLY dbo.fn_AccountBalance(a.Id) AS b
                          LEFT JOIN (SELECT AccountId, SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END) AS Total
                                     FROM dbo.LedgerEntries
                                     GROUP BY AccountId) AS raw ON raw.AccountId = a.Id
                          WHERE b.Balance <> ISNULL(raw.Total, 0)) AS m
                    ORDER BY m.AccountId
                    OFFSET 0 ROWS FETCH NEXT @SampleSize ROWS ONLY;

                    -- 4. Per currency, the balances of all accounts together are exactly zero:
                    -- money moves between accounts and is never created or destroyed.
                    SELECT a.Currency, SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END) AS Net
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.Accounts AS a ON a.Id = e.AccountId
                    GROUP BY a.Currency
                    ORDER BY a.Currency;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetReconciliationReport;");
        }
    }
}
