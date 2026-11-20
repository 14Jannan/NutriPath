using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('" KnowledgeChunks"') IS NOT NULL THEN
                        IF to_regclass('"KnowledgeChunks"') IS NULL THEN
                            ALTER TABLE " KnowledgeChunks" RENAME TO "KnowledgeChunks";
                        ELSE
                            INSERT INTO "KnowledgeChunks" ("Id", "Title", "Category", "Content")
                            SELECT "Id", "Title", "Category", "Content" FROM " KnowledgeChunks"
                            ON CONFLICT ("Id") DO NOTHING;
                        END IF;
                    END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "KnowledgeChunks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KnowledgeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Organization = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Approved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_DocumentId",
                table: "KnowledgeChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_ContentHash",
                table: "KnowledgeDocuments",
                column: "ContentHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeChunks_KnowledgeDocuments_DocumentId",
                table: "KnowledgeChunks",
                column: "DocumentId",
                principalTable: "KnowledgeDocuments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeChunks_KnowledgeDocuments_DocumentId",
                table: "KnowledgeChunks");

            migrationBuilder.DropTable(
                name: "KnowledgeDocuments");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeChunks_DocumentId",
                table: "KnowledgeChunks");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "KnowledgeChunks");
        }
    }
}
