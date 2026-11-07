using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendUserRoleCheckForStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_role",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_role",
                table: "users",
                sql: "role IN ('user','admin','manager','supportstaff','contentstaff')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_role",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_role",
                table: "users",
                sql: "role IN ('user','admin')");
        }
    }
}
