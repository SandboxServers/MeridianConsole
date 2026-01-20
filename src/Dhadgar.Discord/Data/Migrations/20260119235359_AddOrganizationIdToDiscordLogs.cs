using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Dhadgar.Discord.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationIdToDiscordLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "NotificationLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_OrganizationId",
                table: "NotificationLogs",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_OrganizationId",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "NotificationLogs");
        }
    }
}
