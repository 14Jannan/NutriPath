using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "AiMessages",
                newName: "ConversationId");

            migrationBuilder.AddColumn<string>(
                name: "RetrievedContextJson",
                table: "AiMessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiConversations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_ConversationId",
                table: "AiMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_UserId",
                table: "AiConversations",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiMessages_AiConversations_ConversationId",
                table: "AiMessages",
                column: "ConversationId",
                principalTable: "AiConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiMessages_AiConversations_ConversationId",
                table: "AiMessages");

            migrationBuilder.DropTable(
                name: "AiConversations");

            migrationBuilder.DropTable(
                name: "KnowledgeChunks");

            migrationBuilder.DropIndex(
                name: "IX_AiMessages_ConversationId",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "RetrievedContextJson",
                table: "AiMessages");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "AiMessages",
                newName: "UserId");
        }
    }
}
