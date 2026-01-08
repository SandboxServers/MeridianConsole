using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhadgar.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeNormalizedEmailUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the existing non-unique index
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            // Create a new unique index
            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique index
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            // Recreate the non-unique index
            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "NormalizedEmail");
        }
    }
}
