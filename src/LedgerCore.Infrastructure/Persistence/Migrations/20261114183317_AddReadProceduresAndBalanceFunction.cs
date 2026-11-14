using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 4: the read side moves into the database. The balance has one definition,
    /// fn_AccountBalance, used by every read and by the overdraft check; the statement and the
    /// two reports become stored procedures, so their plans can be tuned and measured in one place.
    /// </summary>
    public partial class AddReadProceduresAndBalanceFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An inline table-valued function, not a scalar one: the optimizer expands it into
            // the calling query like a view, so it can seek, join and run in parallel.
            migrationBuilder.Sql("""
                CREATE OR ALTER FUNCTION dbo.fn_AccountBalance (@AccountId uniqueidentifier)
                RETURNS TABLE
                AS
                RETURN
                    SELECT COALESCE(SUM(CASE Direction WHEN 2 THEN Amount ELSE -Amount END), 0) AS Balance
                    FROM dbo.LedgerEntries
                    WHERE AccountId = @AccountId;
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetAccountStatement
                    @AccountId uniqueidentifier,
                    @FromUtc   datetime2(3),
                    @ToUtc     datetime2(3)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    -- Two result sets in one round trip: the account with its opening balance,
                    -- then the entries in [@FromUtc, @ToUtc) with a running balance, computed by
                    -- a window function in one ordered pass.
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
                END
                """);

            // The reports take an optional currency. "@Currency IS NULL OR ..." would get one
            // cached plan for both cases; RECOMPILE plans each call for the values it has, which
            // costs a few milliseconds on a report that runs rarely.
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetDailyVolume
                    @FromUtc  datetime2(3),
                    @ToUtc    datetime2(3),
                    @Currency char(3) = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;

                    -- Money moved per day, currency and transaction type. A transaction's amount
                    -- is the sum of its debit entries, which equals its credits (net-zero rule).
                    SELECT CAST(e.CreatedAtUtc AS date) AS Day,
                           a.Currency,
                           t.Type,
                           COUNT_BIG(DISTINCT e.TransactionId) AS Transactions,
                           SUM(e.Amount) AS Volume
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
                    JOIN dbo.Accounts AS a ON a.Id = e.AccountId
                    WHERE e.Direction = 1
                      AND e.CreatedAtUtc >= @FromUtc
                      AND e.CreatedAtUtc < @ToUtc
                      AND (@Currency IS NULL OR a.Currency = @Currency)
                    GROUP BY CAST(e.CreatedAtUtc AS date), a.Currency, t.Type
                    ORDER BY Day, a.Currency, t.Type
                    OPTION (RECOMPILE);
                END
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetTopAccountsByVolume
                    @FromUtc  datetime2(3),
                    @ToUtc    datetime2(3),
                    @Currency char(3) = NULL,
                    @Top      int = 10
                AS
                BEGIN
                    SET NOCOUNT ON;

                    -- Customer accounts only: settlement and fee revenue accounts would top
                    -- every list. Volume is money in plus money out.
                    SELECT TOP (@Top)
                           e.AccountId,
                           a.Currency,
                           COUNT_BIG(*) AS Entries,
                           SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE 0 END) AS MoneyIn,
                           SUM(CASE e.Direction WHEN 1 THEN e.Amount ELSE 0 END) AS MoneyOut,
                           SUM(e.Amount) AS Volume
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.Accounts AS a ON a.Id = e.AccountId
                    WHERE a.Kind = 1
                      AND e.CreatedAtUtc >= @FromUtc
                      AND e.CreatedAtUtc < @ToUtc
                      AND (@Currency IS NULL OR a.Currency = @Currency)
                    GROUP BY e.AccountId, a.Currency
                    ORDER BY Volume DESC, e.AccountId
                    OPTION (RECOMPILE);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetTopAccountsByVolume;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetDailyVolume;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetAccountStatement;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_AccountBalance;");
        }
    }
}
