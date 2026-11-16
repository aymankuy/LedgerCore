using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 4: the statement is paged by keyset (ADR 0010). A 30-day statement of a hot account
    /// had 7,667 lines and took 58 ms. Nearly all of that was returning the lines, so the fix
    /// was to return fewer of them per call.
    /// </summary>
    public partial class PageAccountStatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                              - COALESCE((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.usp_GetAccountStatement
                    @AccountId uniqueidentifier,
                    @FromUtc   datetime2(3),
                    @ToUtc     datetime2(3)
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @OpeningBalance decimal(19, 4) =
                        (SELECT b.Balance
                              - COALESCE((SELECT SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END)
                                          FROM dbo.LedgerEntries AS e
                                          WHERE e.AccountId = @AccountId AND e.CreatedAtUtc >= @FromUtc), 0)
                         FROM dbo.fn_AccountBalance(@AccountId) AS b);

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
        }
    }
}
