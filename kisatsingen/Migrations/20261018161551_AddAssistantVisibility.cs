using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing assistants were only ever their owner's; an empty value
            // would not read back as a visibility.
            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Assistants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Private");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Assistants");
        }
    }
}
