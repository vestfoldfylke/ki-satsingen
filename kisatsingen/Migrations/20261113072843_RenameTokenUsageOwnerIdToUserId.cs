using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class RenameTokenUsageOwnerIdToUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "TokenUsages",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_TokenUsages_OwnerId_Timestamp",
                table: "TokenUsages",
                newName: "IX_TokenUsages_UserId_Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "TokenUsages",
                newName: "OwnerId");

            migrationBuilder.RenameIndex(
                name: "IX_TokenUsages_UserId_Timestamp",
                table: "TokenUsages",
                newName: "IX_TokenUsages_OwnerId_Timestamp");
        }
    }
}
