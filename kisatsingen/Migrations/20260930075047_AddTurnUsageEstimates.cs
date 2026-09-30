using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnUsageEstimates : Migration
    {
        // Hand-written: EF scaffolds this as renaming TotalTokens to EstimatedOutputTokens,
        // which would show every stored total as an estimate.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "ChatTurns");

            migrationBuilder.AddColumn<long>(
                name: "EstimatedInputTokens",
                table: "ChatTurns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedOutputTokens",
                table: "ChatTurns",
                type: "bigint",
                nullable: true);
        }

        // Rebuilt from the parts, which is what every provider here reported it as,
        // so a rolled-back build still shows a total.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedInputTokens",
                table: "ChatTurns");

            migrationBuilder.DropColumn(
                name: "EstimatedOutputTokens",
                table: "ChatTurns");

            migrationBuilder.AddColumn<long>(
                name: "TotalTokens",
                table: "ChatTurns",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ChatTurns"
                SET "TotalTokens" = COALESCE("InputTokens", 0) + COALESCE("OutputTokens", 0)
                WHERE "InputTokens" IS NOT NULL OR "OutputTokens" IS NOT NULL;
                """);
        }
    }
}
