using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level guarantees that EF Core cannot express in the model:
    /// ledger entries are append-only, and the entries of a transaction always net to zero.
    /// </summary>
    public partial class AddLedgerIntegrityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Spec rule 2: corrections are new reversing entries, never edits.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_LedgerEntries_AppendOnly
                ON dbo.LedgerEntries
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 50001, 'LedgerEntries is append-only. Post a reversing transaction instead.', 1;
                END
                """);

            // Spec rule 3. SQL Server has no deferred constraints, so this only works when all
            // entries of a transaction arrive in one INSERT statement (see usp_PostLedgerTransaction).
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_LedgerEntries_NetZero
                ON dbo.LedgerEntries
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF EXISTS (
                        SELECT 1
                        FROM dbo.LedgerEntries AS e
                        WHERE e.TransactionId IN (SELECT i.TransactionId FROM inserted AS i)
                        GROUP BY e.TransactionId
                        HAVING COUNT(*) < 2
                            OR SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END) <> 0)
                    BEGIN
                        THROW 50002, 'Ledger entries of a transaction must net to zero.', 1;
                    END
                END
                """);

            migrationBuilder.Sql("""
                CREATE TYPE dbo.LedgerEntryList AS TABLE
                (
                    AccountId uniqueidentifier NOT NULL,
                    Direction tinyint          NOT NULL,
                    Amount    decimal(19,4)    NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                CREATE PROCEDURE dbo.usp_PostLedgerTransaction
                    @TransactionId uniqueidentifier,
                    @Type          tinyint,
                    @ReferenceId   nvarchar(64),
                    @Description   nvarchar(256),
                    @CreatedAtUtc  datetime2(3),
                    @Entries       dbo.LedgerEntryList READONLY
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    BEGIN TRANSACTION;

                    INSERT INTO dbo.LedgerTransactions (Id, Type, ReferenceId, Description, CreatedAtUtc)
                    VALUES (@TransactionId, @Type, @ReferenceId, @Description, @CreatedAtUtc);

                    INSERT INTO dbo.LedgerEntries (TransactionId, AccountId, Direction, Amount, CreatedAtUtc)
                    SELECT @TransactionId, e.AccountId, e.Direction, e.Amount, @CreatedAtUtc
                    FROM @Entries AS e;

                    COMMIT TRANSACTION;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE dbo.usp_PostLedgerTransaction;");
            migrationBuilder.Sql("DROP TYPE dbo.LedgerEntryList;");
            migrationBuilder.Sql("DROP TRIGGER dbo.TR_LedgerEntries_NetZero;");
            migrationBuilder.Sql("DROP TRIGGER dbo.TR_LedgerEntries_AppendOnly;");
        }
    }
}
