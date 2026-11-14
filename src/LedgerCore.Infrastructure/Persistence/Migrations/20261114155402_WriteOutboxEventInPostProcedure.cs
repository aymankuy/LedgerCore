using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Outbox pattern: the procedure that posts a ledger transaction now also writes its event to
    /// OutboxMessages, inside the same transaction. No code path can post entries without their
    /// event, or write an event for entries that were rolled back.
    /// </summary>
    public partial class WriteOutboxEventInPostProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER PROCEDURE dbo.usp_PostLedgerTransaction
                    @TransactionId uniqueidentifier,
                    @Type          tinyint,
                    @ReferenceId   nvarchar(64),
                    @Description   nvarchar(256),
                    @CreatedAtUtc  datetime2(3),
                    @Entries       dbo.LedgerEntryList READONLY,
                    @EventId       uniqueidentifier,
                    @EventType     nvarchar(256),
                    @EventPayload  nvarchar(max)
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

                    INSERT INTO dbo.OutboxMessages (Id, Type, Payload, OccurredAtUtc)
                    VALUES (@EventId, @EventType, @EventPayload, @CreatedAtUtc);

                    COMMIT TRANSACTION;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER PROCEDURE dbo.usp_PostLedgerTransaction
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
    }
}
