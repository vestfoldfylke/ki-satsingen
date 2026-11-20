using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    // Written by hand: EF generates a dropped and a recreated table for a
    // renamed entity, which would delete every row. Renames keep the rows and
    // only change metadata.
    /// <inheritdoc />
    public partial class RenameTokenUsagesToUsageRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "TokenUsages",
                newName: "UsageRecords");

            migrationBuilder.Sql("""ALTER TABLE "UsageRecords" RENAME CONSTRAINT "PK_TokenUsages" TO "PK_UsageRecords";""");
            migrationBuilder.Sql("""ALTER TABLE "UsageRecords" RENAME CONSTRAINT "ck_token_usages_turn_has_status" TO "ck_usage_records_turn_has_status";""");

            migrationBuilder.RenameIndex(
                name: "IX_TokenUsages_UserId_Timestamp",
                table: "UsageRecords",
                newName: "IX_UsageRecords_UserId_Timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_TokenUsages_AssistantId_Timestamp",
                table: "UsageRecords",
                newName: "IX_UsageRecords_AssistantId_Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_UsageRecords_AssistantId_Timestamp",
                table: "UsageRecords",
                newName: "IX_TokenUsages_AssistantId_Timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_UsageRecords_UserId_Timestamp",
                table: "UsageRecords",
                newName: "IX_TokenUsages_UserId_Timestamp");

            migrationBuilder.Sql("""ALTER TABLE "UsageRecords" RENAME CONSTRAINT "ck_usage_records_turn_has_status" TO "ck_token_usages_turn_has_status";""");
            migrationBuilder.Sql("""ALTER TABLE "UsageRecords" RENAME CONSTRAINT "PK_UsageRecords" TO "PK_TokenUsages";""");

            migrationBuilder.RenameTable(
                name: "UsageRecords",
                newName: "TokenUsages");
        }
    }
}
