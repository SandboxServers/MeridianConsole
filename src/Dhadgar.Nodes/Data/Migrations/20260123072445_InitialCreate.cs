using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Nodes.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrollment_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedByNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollment_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_certificates_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "node_capacities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxGameServers = table.Column<int>(type: "integer", nullable: false),
                    CurrentGameServers = table.Column<int>(type: "integer", nullable: false),
                    AvailableMemoryBytes = table.Column<long>(type: "bigint", nullable: false),
                    AvailableDiskBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_capacities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_node_capacities_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "node_hardware_inventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CpuCores = table.Column<int>(type: "integer", nullable: false),
                    MemoryBytes = table.Column<long>(type: "bigint", nullable: false),
                    DiskBytes = table.Column<long>(type: "bigint", nullable: false),
                    NetworkInterfaces = table.Column<string>(type: "jsonb", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_hardware_inventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_node_hardware_inventories_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "node_healths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CpuUsagePercent = table.Column<double>(type: "double precision", nullable: false),
                    MemoryUsagePercent = table.Column<double>(type: "double precision", nullable: false),
                    DiskUsagePercent = table.Column<double>(type: "double precision", nullable: false),
                    ActiveGameServers = table.Column<int>(type: "integer", nullable: false),
                    HealthIssues = table.Column<string>(type: "jsonb", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_healths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_node_healths_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_certificates_expiry",
                table: "agent_certificates",
                column: "NotAfter");

            migrationBuilder.CreateIndex(
                name: "ix_agent_certificates_node_active",
                table: "agent_certificates",
                columns: new[] { "NodeId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_certificates_thumbprint",
                table: "agent_certificates",
                column: "Thumbprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_expires",
                table: "enrollment_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_hash",
                table: "enrollment_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_org_expires",
                table: "enrollment_tokens",
                columns: new[] { "OrganizationId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "ix_node_capacities_availability",
                table: "node_capacities",
                columns: new[] { "MaxGameServers", "CurrentGameServers" });

            migrationBuilder.CreateIndex(
                name: "ix_node_capacities_node_id",
                table: "node_capacities",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_node_hardware_inventories_node_id",
                table: "node_hardware_inventories",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_node_healths_node_id",
                table: "node_healths",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_node_healths_reported_at",
                table: "node_healths",
                column: "ReportedAt");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_active",
                table: "nodes",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_last_heartbeat",
                table: "nodes",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_org_name_unique",
                table: "nodes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nodes_org_status",
                table: "nodes",
                columns: new[] { "OrganizationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_certificates");

            migrationBuilder.DropTable(
                name: "enrollment_tokens");

            migrationBuilder.DropTable(
                name: "node_capacities");

            migrationBuilder.DropTable(
                name: "node_hardware_inventories");

            migrationBuilder.DropTable(
                name: "node_healths");

            migrationBuilder.DropTable(
                name: "nodes");
        }
    }
}
