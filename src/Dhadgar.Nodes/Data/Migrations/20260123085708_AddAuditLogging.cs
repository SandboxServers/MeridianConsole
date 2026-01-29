using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Nodes.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "nodes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "HealthScore",
                table: "node_healths",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "HealthTrend",
                table: "node_healths",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScoreChange",
                table: "node_healths",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "capacity_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationToken = table.Column<Guid>(type: "uuid", nullable: false),
                    MemoryMb = table.Column<int>(type: "integer", nullable: false),
                    DiskMb = table.Column<int>(type: "integer", nullable: false),
                    CpuMillicores = table.Column<int>(type: "integer", nullable: false),
                    ServerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capacity_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_capacity_reservations_nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "node_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorType = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_node_healths_score",
                table: "node_healths",
                column: "HealthScore");

            migrationBuilder.CreateIndex(
                name: "ix_capacity_reservations_expiry",
                table: "capacity_reservations",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_capacity_reservations_node_status",
                table: "capacity_reservations",
                columns: new[] { "NodeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_capacity_reservations_server_id",
                table: "capacity_reservations",
                column: "ServerId",
                filter: "\"ServerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_capacity_reservations_token",
                table: "capacity_reservations",
                column: "ReservationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_action",
                table: "node_audit_logs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_actor",
                table: "node_audit_logs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "ix_audit_correlation",
                table: "node_audit_logs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "ix_audit_org_timestamp",
                table: "node_audit_logs",
                columns: new[] { "OrganizationId", "Timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_outcome",
                table: "node_audit_logs",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "ix_audit_resource_id",
                table: "node_audit_logs",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "ix_audit_timestamp",
                table: "node_audit_logs",
                column: "Timestamp",
                descending: new[] { true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capacity_reservations");

            migrationBuilder.DropTable(
                name: "node_audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_node_healths_score",
                table: "node_healths");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "nodes");

            migrationBuilder.DropColumn(
                name: "HealthScore",
                table: "node_healths");

            migrationBuilder.DropColumn(
                name: "HealthTrend",
                table: "node_healths");

            migrationBuilder.DropColumn(
                name: "LastScoreChange",
                table: "node_healths");
        }
    }
}
