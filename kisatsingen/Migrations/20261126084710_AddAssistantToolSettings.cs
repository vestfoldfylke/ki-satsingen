using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantToolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFileGenerationEnabled",
                table: "Assistants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWebSearchEnabled",
                table: "Assistants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFileGenerationEnabled",
                table: "Assistants");

            migrationBuilder.DropColumn(
                name: "IsWebSearchEnabled",
                table: "Assistants");
        }
    }
}
