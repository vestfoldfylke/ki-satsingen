using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class DropKnowledgeFileOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "KnowledgeFiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hand-written: the scaffolded version gives every row an empty owner.
            // Each file takes its chat's or its assistant's owner instead, as it
            // did before the column was dropped.
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "KnowledgeFiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "KnowledgeFiles" AS f
                SET "OwnerId" = COALESCE(
                    (SELECT c."OwnerId" FROM "Chats" AS c WHERE c."Id" = f."ChatId"),
                    (SELECT a."OwnerId" FROM "Assistants" AS a WHERE a."Id" = f."AssistantId"));
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerId",
                table: "KnowledgeFiles",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
