using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenUsageKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "TokenUsages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TokenUsages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ChatTurn");

            // The default only fills in the existing rows; every new row names its kind.
            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "TokenUsages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "ChatTurn");

            migrationBuilder.AddCheckConstraint(
                name: "ck_token_usages_turn_has_status",
                table: "TokenUsages",
                sql: "\"Kind\" <> 'ChatTurn' OR \"Status\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only turn rows fit the old shape, where every row has a status.
            migrationBuilder.Sql("""DELETE FROM "TokenUsages" WHERE "Kind" <> 'ChatTurn'""");

            migrationBuilder.DropCheckConstraint(
                name: "ck_token_usages_turn_has_status",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TokenUsages");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "TokenUsages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
