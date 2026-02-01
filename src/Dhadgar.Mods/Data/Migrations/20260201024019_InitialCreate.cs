using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dhadgar.Mods.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "mod_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_categories_mod_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "mod_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "mods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    GameType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalDownloads = table.Column<long>(type: "bigint", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ProjectUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mods_mod_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "mod_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "mod_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Major = table.Column<int>(type: "integer", nullable: false),
                    Minor = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<int>(type: "integer", nullable: false),
                    Prerelease = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BuildMetadata = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReleaseNotes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MinGameVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MaxGameVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsLatest = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrerelease = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeprecated = table.Column<bool>(type: "boolean", nullable: false),
                    DeprecationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DownloadCount = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_versions_mods_ModId",
                        column: x => x.ModId,
                        principalTable: "mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mod_compatibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncompatibleWithModId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsUserReported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_compatibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_compatibilities_mod_versions_ModVersionId",
                        column: x => x.ModVersionId,
                        principalTable: "mod_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mod_compatibilities_mods_IncompatibleWithModId",
                        column: x => x.IncompatibleWithModId,
                        principalTable: "mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mod_dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnModId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_dependencies_mod_versions_ModVersionId",
                        column: x => x.ModVersionId,
                        principalTable: "mod_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mod_dependencies_mods_DependsOnModId",
                        column: x => x.DependsOnModId,
                        principalTable: "mods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mod_downloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mod_downloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mod_downloads_mod_versions_ModVersionId",
                        column: x => x.ModVersionId,
                        principalTable: "mod_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_mod_categories_ParentId",
                table: "mod_categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_categories_slug_unique",
                table: "mod_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mod_categories_sort",
                table: "mod_categories",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "ix_mod_compatibilities_incompatible_with",
                table: "mod_compatibilities",
                column: "IncompatibleWithModId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_compatibilities_version",
                table: "mod_compatibilities",
                column: "ModVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_compatibilities_version_mod_unique",
                table: "mod_compatibilities",
                columns: new[] { "ModVersionId", "IncompatibleWithModId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mod_dependencies_depends_on",
                table: "mod_dependencies",
                column: "DependsOnModId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_dependencies_version",
                table: "mod_dependencies",
                column: "ModVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_dependencies_version_mod_unique",
                table: "mod_dependencies",
                columns: new[] { "ModVersionId", "DependsOnModId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mod_downloads_date",
                table: "mod_downloads",
                column: "DownloadedAt");

            migrationBuilder.CreateIndex(
                name: "ix_mod_downloads_org",
                table: "mod_downloads",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_downloads_version",
                table: "mod_downloads",
                column: "ModVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_downloads_version_date",
                table: "mod_downloads",
                columns: new[] { "ModVersionId", "DownloadedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_mod_versions_latest",
                table: "mod_versions",
                columns: new[] { "ModId", "IsLatest" },
                filter: "\"IsLatest\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_mod_versions_mod",
                table: "mod_versions",
                column: "ModId");

            migrationBuilder.CreateIndex(
                name: "ix_mod_versions_mod_version_unique",
                table: "mod_versions",
                columns: new[] { "ModId", "Version" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mod_versions_semver",
                table: "mod_versions",
                columns: new[] { "ModId", "Major", "Minor", "Patch" });

            migrationBuilder.CreateIndex(
                name: "ix_mods_category",
                table: "mods",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "ix_mods_downloads",
                table: "mods",
                column: "TotalDownloads");

            migrationBuilder.CreateIndex(
                name: "ix_mods_gametype",
                table: "mods",
                column: "GameType");

            migrationBuilder.CreateIndex(
                name: "ix_mods_org",
                table: "mods",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "ix_mods_org_slug_unique",
                table: "mods",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_mods_public",
                table: "mods",
                column: "IsPublic",
                filter: "\"IsPublic\" = true AND \"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mod_compatibilities");

            migrationBuilder.DropTable(
                name: "mod_dependencies");

            migrationBuilder.DropTable(
                name: "mod_downloads");

            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "mod_versions");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "mods");

            migrationBuilder.DropTable(
                name: "mod_categories");
        }
    }
}
