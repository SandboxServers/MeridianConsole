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

            // Safety: Remove any duplicate NormalizedEmail values before adding unique constraint.
            // Keeps the oldest user (lowest id) for each duplicate email.
            migrationBuilder.Sql(@"
                DELETE FROM users
                WHERE ""Id"" NOT IN (
                    SELECT MIN(""Id"")
                    FROM users
                    WHERE ""NormalizedEmail"" IS NOT NULL
                    GROUP BY ""NormalizedEmail""
                )
                AND ""NormalizedEmail"" IN (
                    SELECT ""NormalizedEmail""
                    FROM users
                    WHERE ""NormalizedEmail"" IS NOT NULL
                    GROUP BY ""NormalizedEmail""
                    HAVING COUNT(*) > 1
                );
            ");

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
