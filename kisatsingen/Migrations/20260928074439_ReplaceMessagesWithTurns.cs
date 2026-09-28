using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceMessagesWithTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chats are wiped rather than migrated: their messages cannot be
            // regrouped into turns reliably, and a chat left without its turns
            // would open empty. Cascades to chat-scoped knowledge files.
            migrationBuilder.Sql("""DELETE FROM "Chats";""");

            migrationBuilder.DropTable(
                name: "ChatEvents");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropSequence(
                name: "chat_entry_seq");

            migrationBuilder.CreateSequence(
                name: "chat_turn_seq");

            migrationBuilder.CreateTable(
                name: "ChatTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('chat_turn_seq')"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailedAt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AnswerJson = table.Column<string>(type: "text", nullable: false),
                    ServedModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResponseId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FinishReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    TimeToFirstTokenMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatTurns_Chats_ChatId",
                        column: x => x.ChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatTurns_ChatId_Seq",
                table: "ChatTurns",
                columns: new[] { "ChatId", "Seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatTurns");

            migrationBuilder.DropSequence(
                name: "chat_turn_seq");

            migrationBuilder.CreateSequence(
                name: "chat_entry_seq");

            migrationBuilder.CreateTable(
                name: "ChatEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('chat_entry_seq')")
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
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentsJson = table.Column<string>(type: "text", nullable: true),
                    ContentsSchemaVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    FinishReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ModelKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ResponseId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('chat_entry_seq')"),
                    SystemPromptSnapshot = table.Column<string>(type: "text", nullable: true),
                    TimeToFirstTokenMs = table.Column<long>(type: "bigint", nullable: true),
                    TotalTokens = table.Column<long>(type: "bigint", nullable: true)
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
        }
    }
}
