using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantsAndKnowledgeFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssistantId",
                table: "Chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Assistants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assistants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AssistantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    TableOfContents = table.Column<string>(type: "text", nullable: true),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTokenCount = table.Column<int>(type: "integer", nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeFiles", x => x.Id);
                    table.CheckConstraint("ck_knowledge_files_single_scope", "num_nonnulls(\"AssistantId\", \"ChatId\") = 1");
                    table.ForeignKey(
                        name: "FK_KnowledgeFiles_Assistants_AssistantId",
                        column: x => x.AssistantId,
                        principalTable: "Assistants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeFiles_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeFileChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Heading = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    EstimatedTokenCount = table.Column<int>(type: "integer", nullable: false)
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
                name: "IX_Chats_AssistantId",
                table: "Chats",
                column: "AssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_Assistants_OwnerId_UpdatedAt",
                table: "Assistants",
                columns: new[] { "OwnerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFileChunks_KnowledgeFileId_Sequence",
                table: "KnowledgeFileChunks",
                columns: new[] { "KnowledgeFileId", "Sequence" },
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Chats_Assistants_AssistantId",
                table: "Chats",
                column: "AssistantId",
                principalTable: "Assistants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Chats_Assistants_AssistantId",
                table: "Chats");

            migrationBuilder.DropTable(
                name: "KnowledgeFileChunks");

            migrationBuilder.DropTable(
                name: "KnowledgeFiles");

            migrationBuilder.DropTable(
                name: "Assistants");

            migrationBuilder.DropIndex(
                name: "IX_Chats_AssistantId",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "AssistantId",
                table: "Chats");
        }
    }
}
