using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoverPerAccountReadsWithOneIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "Direction", "Amount", "TransactionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId_CreatedAtUtc",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId",
                table: "LedgerEntries",
                column: "AccountId");
        }
    }
}
