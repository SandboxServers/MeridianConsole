using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Dhadgar.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.CreateTable(
                name: "claim_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsSystemClaim = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "linked_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMetadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_linked_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalAuthId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    HasPasskeysRegistered = table.Column<bool>(type: "boolean", nullable: false),
                    LastPasskeyAuthAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAuthenticatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_organizations_PreferredOrganizationId",
                        column: x => x.PreferredOrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "viewer"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_organizations_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_organizations_users_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_organizations_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_organization_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<int>(type: "integer", nullable: false),
                    ClaimValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_organization_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_organization_claims_user_organizations_UserOrganizatio~",
                        column: x => x.UserOrganizationId,
                        principalTable: "user_organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_organization_claims_users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "claim_definitions",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IsSystemClaim", "Name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0001-000000000001"), "organization", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View organization details", true, "org:read" },
                    { new Guid("00000000-0000-0000-0001-000000000002"), "organization", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Update organization settings", true, "org:write" },
                    { new Guid("00000000-0000-0000-0001-000000000003"), "organization", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Delete organization", true, "org:delete" },
                    { new Guid("00000000-0000-0000-0001-000000000004"), "organization", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Manage billing and subscriptions", true, "org:billing" },
                    { new Guid("00000000-0000-0000-0002-000000000001"), "members", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View organization members", true, "members:read" },
                    { new Guid("00000000-0000-0000-0002-000000000002"), "members", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Invite new members", true, "members:invite" },
                    { new Guid("00000000-0000-0000-0002-000000000003"), "members", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Remove members", true, "members:remove" },
                    { new Guid("00000000-0000-0000-0002-000000000004"), "members", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Assign member roles", true, "members:roles" },
                    { new Guid("00000000-0000-0000-0003-000000000001"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View servers", true, "servers:read" },
                    { new Guid("00000000-0000-0000-0003-000000000002"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Create and update servers", true, "servers:write" },
                    { new Guid("00000000-0000-0000-0003-000000000003"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Delete servers", true, "servers:delete" },
                    { new Guid("00000000-0000-0000-0003-000000000004"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Start servers", true, "servers:start" },
                    { new Guid("00000000-0000-0000-0003-000000000005"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Stop servers", true, "servers:stop" },
                    { new Guid("00000000-0000-0000-0003-000000000006"), "servers", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Restart servers", true, "servers:restart" },
                    { new Guid("00000000-0000-0000-0004-000000000001"), "nodes", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View nodes", true, "nodes:read" },
                    { new Guid("00000000-0000-0000-0004-000000000002"), "nodes", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Manage node configuration", true, "nodes:manage" },
                    { new Guid("00000000-0000-0000-0005-000000000001"), "files", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View and download files", true, "files:read" },
                    { new Guid("00000000-0000-0000-0005-000000000002"), "files", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Upload and modify files", true, "files:write" },
                    { new Guid("00000000-0000-0000-0005-000000000003"), "files", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Delete files", true, "files:delete" },
                    { new Guid("00000000-0000-0000-0006-000000000001"), "mods", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "View mods", true, "mods:read" },
                    { new Guid("00000000-0000-0000-0006-000000000002"), "mods", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Install and update mods", true, "mods:write" },
                    { new Guid("00000000-0000-0000-0006-000000000003"), "mods", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Uninstall mods", true, "mods:delete" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_claim_definitions_category",
                table: "claim_definitions",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "ix_claim_definitions_name",
                table: "claim_definitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_provider_account",
                table: "linked_accounts",
                columns: new[] { "Provider", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_user_id",
                table: "linked_accounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_active",
                table: "organizations",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_owner_id",
                table: "organizations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_slug",
                table: "organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_OrganizationId",
                table: "refresh_tokens",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_active",
                table: "refresh_tokens",
                columns: new[] { "UserId", "ExpiresAt" },
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_org_claims_lookup",
                table: "user_organization_claims",
                columns: new[] { "UserOrganizationId", "ClaimValue" });

            migrationBuilder.CreateIndex(
                name: "ix_user_org_claims_user_org_id",
                table: "user_organization_claims",
                column: "UserOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_user_organization_claims_GrantedByUserId",
                table: "user_organization_claims",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_user_organizations_active_membership",
                table: "user_organizations",
                columns: new[] { "UserId", "OrganizationId" },
                unique: true,
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_organizations_InvitedByUserId",
                table: "user_organizations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_user_organizations_organization_id",
                table: "user_organizations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "ix_users_active",
                table: "users",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_auth_id",
                table: "users",
                column: "ExternalAuthId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_PreferredOrganizationId",
                table: "users",
                column: "PreferredOrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_linked_accounts_users_UserId",
                table: "linked_accounts",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_organizations_users_OwnerId",
                table: "organizations",
                column: "OwnerId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_organizations_users_OwnerId",
                table: "organizations");

            migrationBuilder.DropTable(
                name: "claim_definitions");

            migrationBuilder.DropTable(
                name: "linked_accounts");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_organization_claims");

            migrationBuilder.DropTable(
                name: "user_organizations");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
