using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedInputTokensAndUsageLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssistantId",
                table: "TokenUsages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CachedInputTokens",
                table: "TokenUsages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChatId",
                table: "TokenUsages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TurnId",
                table: "TokenUsages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CachedInputTokens",
                table: "ChatTurns",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistantId",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "CachedInputTokens",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "ChatId",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "TurnId",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "CachedInputTokens",
                table: "ChatTurns");
        }
    }
}
