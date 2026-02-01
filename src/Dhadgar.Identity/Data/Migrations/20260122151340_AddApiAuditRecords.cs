using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApiAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add AvatarUrl column only if it doesn't already exist (idempotent)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'users' AND column_name = 'AvatarUrl'
                    ) THEN
                        ALTER TABLE users ADD COLUMN "AvatarUrl" character varying(500);
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "api_audit_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ServiceName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_audit_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_resource_time",
                table: "api_audit_records",
                columns: new[] { "ResourceType", "ResourceId", "TimestampUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_tenant_time",
                table: "api_audit_records",
                columns: new[] { "TenantId", "TimestampUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_timestamp",
                table: "api_audit_records",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_user_time",
                table: "api_audit_records",
                columns: new[] { "UserId", "TimestampUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_audit_records");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "users");
        }
    }
}
