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
            // Check for existing rows and fail if any exist without a valid OrganizationId mapping
            // This is a new service, so there shouldn't be any existing data
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ""NotificationLogs"" LIMIT 1) THEN
                        RAISE EXCEPTION 'Cannot add non-nullable OrganizationId: existing rows need migration strategy';
                    END IF;
                END $$;
            ");

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
