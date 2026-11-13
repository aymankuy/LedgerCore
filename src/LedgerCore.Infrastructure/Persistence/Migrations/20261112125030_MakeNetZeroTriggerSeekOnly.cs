using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Stops TR_LedgerEntries_NetZero from deadlocking under parallel posts. The old trigger let
    /// the optimizer scan IX_LedgerEntries_TransactionId, so it waited on other transactions'
    /// uncommitted rows while they waited on its own. Now it seeks only the rows of the
    /// transactions it inserted, and the covering index means no lookup into the clustered index.
    /// </summary>
    public partial class MakeNetZeroTriggerSeekOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_TransactionId",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransactionId",
                table: "LedgerEntries",
                column: "TransactionId")
                .Annotation("SqlServer:Include", new[] { "Direction", "Amount" });

            migrationBuilder.Sql("""
                ALTER TRIGGER dbo.TR_LedgerEntries_NetZero
                ON dbo.LedgerEntries
                AFTER INSERT
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF EXISTS (
                        SELECT 1
                        FROM (SELECT DISTINCT i.TransactionId FROM inserted AS i) AS t
                        JOIN dbo.LedgerEntries AS e WITH (FORCESEEK)
                            ON e.TransactionId = t.TransactionId
                        GROUP BY e.TransactionId
                        HAVING COUNT(*) < 2
                            OR SUM(CASE e.Direction WHEN 2 THEN e.Amount ELSE -e.Amount END) <> 0)
                    BEGIN
                        THROW 50002, 'Ledger entries of a transaction must net to zero.', 1;
                    END
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TRIGGER dbo.TR_LedgerEntries_NetZero
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

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_TransactionId",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransactionId",
                table: "LedgerEntries",
                column: "TransactionId");
        }
    }
}
