using System;
using Dhadgar.Nodes.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Nodes.Data.Migrations
{
    /// <summary>
    /// Adds the xmin optimistic concurrency token to agent_certificates so that a certificate renewal
    /// cannot race a concurrent renewal, revoke or decommission of the same certificate.
    /// </summary>
    [DbContext(typeof(NodesDbContext))]
    [Migration("20261027120000_AddAgentCertificateRowVersion")]
    public partial class AddAgentCertificateRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "agent_certificates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "agent_certificates");
        }
    }
}
