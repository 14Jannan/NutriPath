using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMealLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Meals",
                newName: "MealType");

            migrationBuilder.RenameColumn(
                name: "Servings",
                table: "MealItems",
                newName: "SugarGramsSnapshot");

            migrationBuilder.AddColumn<decimal>(
                name: "CaloriesSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CarbsGramsSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FatGramsSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FiberGramsSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoggedAtUtc",
                table: "MealItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "ProteinGramsSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityGrams",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SodiumMilligramsSnapshot",
                table: "MealItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Meals_UserId",
                table: "Meals",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Meals_Users_UserId",
                table: "Meals",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Meals_Users_UserId",
                table: "Meals");

            migrationBuilder.DropIndex(
                name: "IX_Meals_UserId",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "CaloriesSnapshot",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "CarbsGramsSnapshot",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "FatGramsSnapshot",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "FiberGramsSnapshot",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "LoggedAtUtc",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "ProteinGramsSnapshot",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "QuantityGrams",
                table: "MealItems");

            migrationBuilder.DropColumn(
                name: "SodiumMilligramsSnapshot",
                table: "MealItems");

            migrationBuilder.RenameColumn(
                name: "MealType",
                table: "Meals",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "SugarGramsSnapshot",
                table: "MealItems",
                newName: "Servings");
        }
    }
}
