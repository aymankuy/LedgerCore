using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6: what the warehouse load reads from the ledger. The SSIS package, the Azure Data
    /// Factory pipeline and the tests all call these procedures, so there is one definition of
    /// what leaves the OLTP database and which entries are safe to take.
    /// </summary>
    public partial class AddWarehouseExtractProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The upper bound of an extract. Entry ids are handed out at INSERT but commit in any
            // order, so MAX(Id) can include an id above one that is still uncommitted; a load that
            // stopped there would skip that entry forever. Taking the posting lock exclusively waits
            // for every posting in flight to commit, exactly as the balance snapshot refresh does
            // (ADR 0009), and every later posting gets a higher id.
            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.usp_GetSafeEntryWatermark
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    BEGIN TRANSACTION;

                    DECLARE @lock int;
                    EXEC @lock = sp_getapplock @Resource = N'LedgerCore.LedgerPosting', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
                    IF @lock < 0
                        THROW 50012, 'Timed out waiting for in-flight postings; try the extract again.', 1;

                    SELECT COALESCE(MAX(Id), 0) AS SafeEntryId FROM dbo.LedgerEntries;

                    COMMIT TRANSACTION;
                END
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.usp_ExtractLedgerEntries
                    @FromEntryId bigint,
                    @ToEntryId   bigint
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT e.Id AS EntryId, e.TransactionId, e.AccountId, t.Type AS TransactionType,
                           e.Direction, e.Amount, e.CreatedAtUtc
                    FROM dbo.LedgerEntries AS e
                    JOIN dbo.LedgerTransactions AS t ON t.Id = e.TransactionId
                    WHERE e.Id > @FromEntryId AND e.Id <= @ToEntryId
                    ORDER BY e.Id;
                END
                """);

            // All of it, every time: one row per status an account has had, so it is as small as
            // the number of accounts. See etl.usp_LoadDimAccount for why this needs no watermark.
            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.usp_ExtractAccountStatusHistory
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT h.Id AS HistoryId, h.AccountId, a.OwnerUserId, a.Currency, a.Kind,
                           a.AllowsOverdraft, h.Status, h.ChangedAtUtc
                    FROM dbo.AccountStatusHistory AS h
                    JOIN dbo.Accounts AS a ON a.Id = h.AccountId
                    ORDER BY h.Id;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE dbo.usp_ExtractAccountStatusHistory;");
            migrationBuilder.Sql("DROP PROCEDURE dbo.usp_ExtractLedgerEntries;");
            migrationBuilder.Sql("DROP PROCEDURE dbo.usp_GetSafeEntryWatermark;");
        }
    }
}
