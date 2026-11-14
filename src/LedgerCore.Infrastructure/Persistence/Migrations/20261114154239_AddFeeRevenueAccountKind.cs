using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeRevenueAccountKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "UX_Accounts_FeeRevenuePerCurrency",
                table: "Accounts",
                column: "Currency",
                unique: true,
                filter: "[Kind] = 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts",
                sql: "[Kind] IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Accounts_FeeRevenuePerCurrency",
                table: "Accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_Kind",
                table: "Accounts",
                sql: "[Kind] IN (1, 2)");
        }
    }
}
