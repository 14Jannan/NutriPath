using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFoods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Foods",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foods_CreatedByUserId",
                table: "Foods",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Foods_Users_CreatedByUserId",
                table: "Foods",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Foods_Users_CreatedByUserId",
                table: "Foods");

            migrationBuilder.DropIndex(
                name: "IX_Foods_CreatedByUserId",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Foods");
        }
    }
}
