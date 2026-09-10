using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "chat_entry_seq");

            migrationBuilder.CreateTable(
                name: "Chats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('chat_entry_seq')"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatEvents_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('chat_entry_seq')"),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentsJson = table.Column<string>(type: "text", nullable: true),
                    ContentsSchemaVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponseId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FinishReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    TimeToFirstTokenMs = table.Column<long>(type: "bigint", nullable: true),
                    SystemPromptSnapshot = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEvents_ChatId_Seq",
                table: "ChatEvents",
                columns: new[] { "ChatId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_Seq",
                table: "ChatMessages",
                columns: new[] { "ChatId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_Chats_OwnerId_UpdatedAt",
                table: "Chats",
                columns: new[] { "OwnerId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatEvents");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "Chats");

            migrationBuilder.DropSequence(
                name: "chat_entry_seq");
        }
    }
}
