using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// COALESCE((subquery), 0) is expanded to CASE WHEN (subquery) IS NOT NULL THEN (subquery)
    /// ELSE 0 END, so SQL Server ran each balance subquery twice. The actual plans showed it:
    /// two seeks for the snapshot and two for the entries after the watermark. ISNULL
    /// evaluates its argument once. The definitions are otherwise unchanged.
    /// </summary>
    public partial class EvaluateBalanceSubqueriesOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BalanceFunction("ISNULL"));
            migrationBuilder.Sql(StatementProcedure("ISNULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BalanceFunction("COALESCE"));
            migrationBuilder.Sql(StatementProcedure("COALESCE"));
        }

        private static string BalanceFunction(string orZero) => $"""
            CREATE OR ALTER FUNCTION dbo.fn_AccountBalance (@AccountId uniqueidentifier)
            RETURNS TABLE
            AS
            RETURN
                SELECT {orZero}((SELECT s.Balance FROM dbo.AccountBalanceSnapshots AS s WHERE s.AccountId = @AccountId), 0)
                     + {orZero}((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
                                 FROM dbo.LedgerEntries AS e WITH (INDEX(1), FORCESEEK)
                                 WHERE e.Id > w.ToEntryId AND e.AccountId = @AccountId), 0) AS Balance
                FROM (SELECT {orZero}((SELECT TOP (1) r.ToEntryId FROM dbo.BalanceSnapshotRuns AS r ORDER BY r.Id DESC), 0) AS ToEntryId) AS w;
            """;

        private static string StatementProcedure(string orZero) => $"""
            CREATE OR ALTER PROCEDURE dbo.usp_GetAccountStatement
                @AccountId    uniqueidentifier,
                @FromUtc      datetime2(3),
                @ToUtc        datetime2(3),
                @PageSize     int,
                @AfterUtc     datetime2(3) = NULL,
                @AfterEntryId bigint = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                -- The page starts strictly after (@StartUtc, @StartId) in statement order:
                -- after the cursor's line, or for the first page at @FromUtc itself
                -- (identity values start at 1, so Id > 0 excludes nothing).
                DECLARE @StartUtc datetime2(3) = COALESCE(@AfterUtc, @FromUtc),
                        @StartId  bigint = COALESCE(@AfterEntryId, 0);

                -- The balance just before the page: the current balance minus every entry
                -- from the page's start onwards, read in one statement. The redundant
                -- "CreatedAtUtc >= @StartUtc" lets both queries seek the per-account index.
                DECLARE @OpeningBalance decimal(19, 4) =
                    (SELECT b.Balance
                          - {orZero}((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
                                      FROM dbo.LedgerEntries AS e
                                      WHERE e.AccountId = @AccountId
                                        AND e.CreatedAtUtc >= @StartUtc
                                        AND (e.CreatedAtUtc > @StartUtc OR e.Id > @StartId)), 0)
                     FROM dbo.fn_AccountBalance(@AccountId) AS b);

                SELECT Id AS AccountId, Currency, @OpeningBalance AS OpeningBalance
                FROM dbo.Accounts
                WHERE Id = @AccountId;

                -- TOP runs first, in the inner query, so only the page's lines are joined
                -- to their transactions. The running balance is then taken over those lines.
                SELECT p.EntryId,
                       p.TransactionId,
                       p.Type,
                       p.ReferenceId,
                       p.Description,
                       p.Amount,
                       @OpeningBalance + SUM(p.Amount) OVER (ORDER BY p.CreatedAtUtc, p.EntryId ROWS UNBOUNDED PRECEDING) AS BalanceAfter,
                       p.CreatedAtUtc
                FROM (
                    SELECT TOP (@PageSize + 1)
                           e.Id AS EntryId,
                           e.TransactionId,
                           t.Type,
                           t.ReferenceId,
                           t.Description,
                           CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END AS Amount,
                           e.CreatedAtUtc
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
                    WHERE e.AccountId = @AccountId
                      AND e.CreatedAtUtc >= @StartUtc
                      AND (e.CreatedAtUtc > @StartUtc OR e.Id > @StartId)
                      AND e.CreatedAtUtc < @ToUtc
                    ORDER BY e.CreatedAtUtc, e.Id
                ) AS p
                ORDER BY p.CreatedAtUtc, p.EntryId;
            END
            """;
    }
}
