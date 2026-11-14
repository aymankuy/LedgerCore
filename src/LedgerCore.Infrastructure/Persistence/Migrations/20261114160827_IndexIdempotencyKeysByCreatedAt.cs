using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexIdempotencyKeysByCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_CreatedAtUtc",
                table: "IdempotencyKeys",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IdempotencyKeys_CreatedAtUtc",
                table: "IdempotencyKeys");
        }
    }
}
