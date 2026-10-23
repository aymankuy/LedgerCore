using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                table: "Accounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.CreateIndex(
                name: "UX_Accounts_SettlementPerCurrency",
                table: "Accounts",
                column: "Currency",
                unique: true,
                filter: "[Kind] = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts",
                sql: "[Kind] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Accounts_SettlementPerCurrency",
                table: "Accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Accounts");
        }
    }
}
