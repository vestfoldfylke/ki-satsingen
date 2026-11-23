using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageRecordWebSearchCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WebSearchCount",
                table: "UsageRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_usage_records_web_search_has_count",
                table: "UsageRecords",
                sql: "(\"Kind\" = 'WebSearch') = (\"WebSearchCount\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_usage_records_web_search_has_count",
                table: "UsageRecords");

            migrationBuilder.DropColumn(
                name: "WebSearchCount",
                table: "UsageRecords");
        }
    }
}
