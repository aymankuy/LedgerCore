using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountStatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountStatusHistory", x => x.Id);
                    table.CheckConstraint("CK_AccountStatusHistory_Status", "[Status] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_AccountStatusHistory_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountStatusHistory_AccountId_Id",
                table: "AccountStatusHistory",
                columns: new[] { "AccountId", "Id" });

            // Accounts that exist before this migration get one row with their current status at
            // creation time. When they changed status earlier, that moment was never recorded.
            migrationBuilder.Sql("""
                INSERT INTO dbo.AccountStatusHistory (AccountId, Status, ChangedAtUtc)
                SELECT Id, Status, CreatedAtUtc FROM dbo.Accounts ORDER BY CreatedAtUtc, Id;
                """);

            // The history is written by the database, in the transaction that changes the status,
            // so no code path can change a status without leaving a trace. Opening an account
            // records its first status at CreatedAtUtc; later changes are stamped with the
            // database clock.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_Accounts_StatusHistory
                ON dbo.Accounts
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    INSERT INTO dbo.AccountStatusHistory (AccountId, Status, ChangedAtUtc)
                    SELECT i.Id, i.Status, CASE WHEN d.Id IS NULL THEN i.CreatedAtUtc ELSE SYSUTCDATETIME() END
                    FROM inserted AS i
                    LEFT JOIN deleted AS d ON d.Id = i.Id
                    WHERE d.Id IS NULL OR d.Status <> i.Status;
                END
                """);

            // Same rule as the ledger: history is corrected by appending, never by editing.
            migrationBuilder.Sql("""
                CREATE TRIGGER dbo.TR_AccountStatusHistory_AppendOnly
                ON dbo.AccountStatusHistory
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 50020, 'AccountStatusHistory is append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER dbo.TR_AccountStatusHistory_AppendOnly;");
            migrationBuilder.Sql("DROP TRIGGER dbo.TR_Accounts_StatusHistory;");

            migrationBuilder.DropTable(
                name: "AccountStatusHistory");
        }
    }
}
