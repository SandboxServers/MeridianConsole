using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Nodes.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCertificateGraceUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GraceUntil",
                table: "agent_certificates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraceUntil",
                table: "agent_certificates");
        }
    }
}
