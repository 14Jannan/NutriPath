using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodDataPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Authority = table.Column<string>(type: "text", nullable: false),
                    License = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    ServingSizeGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    Calories = table.Column<decimal>(type: "numeric", nullable: false),
                    ProteinGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    CarbsGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    FatGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    FiberGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    SugarGrams = table.Column<decimal>(type: "numeric", nullable: false),
                    SodiumMilligrams = table.Column<decimal>(type: "numeric", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Foods_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecordsFetched = table.Column<int>(type: "integer", nullable: false),
                    RecordsInserted = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncJobs_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Foods_DataSourceId_ExternalId",
                table: "Foods",
                columns: new[] { "DataSourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_DataSourceId",
                table: "SyncJobs",
                column: "DataSourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Foods");

            migrationBuilder.DropTable(
                name: "SyncJobs");

            migrationBuilder.DropTable(
                name: "DataSources");
        }
    }
}
