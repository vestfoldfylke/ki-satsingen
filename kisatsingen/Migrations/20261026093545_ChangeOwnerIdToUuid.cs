using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace kisatsingen.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOwnerIdToUuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written: AlterColumn emits no USING clause, and PostgreSQL has
            // no implicit cast from varchar to uuid, so the scaffolded version fails.
            migrationBuilder.Sql("""ALTER TABLE "TokenUsages" ALTER COLUMN "OwnerId" TYPE uuid USING "OwnerId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "KnowledgeFiles" ALTER COLUMN "OwnerId" TYPE uuid USING "OwnerId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Chats" ALTER COLUMN "OwnerId" TYPE uuid USING "OwnerId"::uuid;""");
            migrationBuilder.Sql("""ALTER TABLE "Assistants" ALTER COLUMN "OwnerId" TYPE uuid USING "OwnerId"::uuid;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "TokenUsages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "KnowledgeFiles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "Chats",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerId",
                table: "Assistants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
