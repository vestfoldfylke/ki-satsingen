using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddReasoningTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReasoningTokens",
                table: "TokenUsages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReasoningTokens",
                table: "ChatTurns",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "ChatTurns");
        }
    }
}
