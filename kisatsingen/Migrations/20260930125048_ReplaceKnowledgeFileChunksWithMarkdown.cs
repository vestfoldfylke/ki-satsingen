using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceKnowledgeFileChunksWithMarkdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeFileChunks");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_AssistantId",
                table: "KnowledgeFiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_ChatId",
                table: "KnowledgeFiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_OwnerId",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "TableOfContents",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "ChunkCount",
                table: "KnowledgeFiles");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "KnowledgeFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "KnowledgeFiles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "ContentOrigin",
                table: "KnowledgeFiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LineCount",
                table: "KnowledgeFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Markdown",
                table: "KnowledgeFiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "KnowledgeFiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "ChatTurns",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_AssistantId_Sha256",
                table: "KnowledgeFiles",
                columns: new[] { "AssistantId", "Sha256" },
                unique: true,
                filter: "\"AssistantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_ChatId_Sha256",
                table: "KnowledgeFiles",
                columns: new[] { "ChatId", "Sha256" },
                unique: true,
                filter: "\"ChatId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_AssistantId_Sha256",
                table: "KnowledgeFiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeFiles_ChatId_Sha256",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "ContentOrigin",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "LineCount",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "Markdown",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "KnowledgeFiles");

            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "ChatTurns");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "KnowledgeFiles");

            migrationBuilder.AddColumn<int>(
                name: "ChunkCount",
                table: "KnowledgeFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "KnowledgeFiles",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "KnowledgeFiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TableOfContents",
                table: "KnowledgeFiles",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KnowledgeFileChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    EstimatedTokenCount = table.Column<int>(type: "integer", nullable: false),
                    Heading = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeFileChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeFileChunks_KnowledgeFiles_KnowledgeFileId",
                        column: x => x.KnowledgeFileId,
                        principalTable: "KnowledgeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_AssistantId",
                table: "KnowledgeFiles",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_ChatId",
                table: "KnowledgeFiles",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_OwnerId",
                table: "KnowledgeFiles",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFileChunks_KnowledgeFileId_Sequence",
                table: "KnowledgeFileChunks",
                columns: new[] { "KnowledgeFileId", "Sequence" },
                unique: true);
        }
    }
}
