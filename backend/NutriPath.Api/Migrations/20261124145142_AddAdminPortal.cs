using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NutriPath.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "Users",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "KnowledgeDocuments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "KnowledgeDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "KnowledgeDocuments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "KnowledgeDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "KnowledgeDocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesDocumentId",
                table: "KnowledgeDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "KnowledgeDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "KnowledgeDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Foods",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Foods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Foods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NameSinhala",
                table: "Foods",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameTamil",
                table: "Foods",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActorRoles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminMfa",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretCiphertext = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedTimeStep = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminMfa", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AdminMfa_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminRecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MfaVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiRequestMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    RetrievalMs = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    ValidationPassed = table.Column<bool>(type: "boolean", nullable: false),
                    Citations = table.Column<int>(type: "integer", nullable: false),
                    InsufficientEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRequestMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResultJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    Tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Hint = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeSettingVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeSettingVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAiLimits",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenLimit = table.Column<int>(type: "integer", nullable: false),
                    WindowHours = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiLimits", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserAiLimits_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.Role });
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_Status",
                table: "KnowledgeDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Action",
                table: "AdminAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_ActorUserId_AtUtc",
                table: "AdminAuditLogs",
                columns: new[] { "ActorUserId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_AtUtc",
                table: "AdminAuditLogs",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_Email",
                table: "AdminInvitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_TokenHash",
                table: "AdminInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminRecoveryCodes_UserId",
                table: "AdminRecoveryCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_RefreshTokenHash",
                table: "AdminSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_UserId",
                table: "AdminSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestMetrics_AtUtc",
                table: "AiRequestMetrics",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestMetrics_UserId_AtUtc",
                table: "AiRequestMetrics",
                columns: new[] { "UserId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_Status_CreatedAtUtc",
                table: "BackgroundJobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentials_Provider_Status",
                table: "ProviderCredentials",
                columns: new[] { "Provider", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentials_Provider_Version",
                table: "ProviderCredentials",
                columns: new[] { "Provider", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeSettingVersions_Key_Version",
                table: "RuntimeSettingVersions",
                columns: new[] { "Key", "Version" },
                unique: true);

            // Existing documents: Approved stays the source of truth for retrieval; mirror it in Status.
            migrationBuilder.Sql("""UPDATE "KnowledgeDocuments" SET "Status" = CASE WHEN "Approved" THEN 'Approved' ELSE 'Draft' END, "CreatedAtUtc" = COALESCE("ReviewedAtUtc", now());""");
            // Existing users get a random security stamp (tokens issued before this carry none and expire normally).
            migrationBuilder.Sql("""UPDATE "Users" SET "SecurityStamp" = md5(random()::text || "Id"::text) WHERE "SecurityStamp" = '';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropTable(
                name: "AdminInvitations");

            migrationBuilder.DropTable(
                name: "AdminMfa");

            migrationBuilder.DropTable(
                name: "AdminRecoveryCodes");

            migrationBuilder.DropTable(
                name: "AdminSessions");

            migrationBuilder.DropTable(
                name: "AiRequestMetrics");

            migrationBuilder.DropTable(
                name: "BackgroundJobs");

            migrationBuilder.DropTable(
                name: "ProviderCredentials");

            migrationBuilder.DropTable(
                name: "RuntimeSettings");

            migrationBuilder.DropTable(
                name: "RuntimeSettingVersions");

            migrationBuilder.DropTable(
                name: "UserAiLimits");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeDocuments_Status",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "SupersedesDocumentId",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "KnowledgeDocuments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "NameSinhala",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "NameTamil",
                table: "Foods");
        }
    }
}
