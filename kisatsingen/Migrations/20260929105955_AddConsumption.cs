using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Consumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenCount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consumptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consumptions_OwnerId_Timestamp",
                table: "Consumptions",
                columns: new[] { "OwnerId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consumptions");
        }
    }
}
