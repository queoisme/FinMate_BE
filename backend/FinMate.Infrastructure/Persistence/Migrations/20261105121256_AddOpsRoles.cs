using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsRoles : Migration
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
                sql: "role IN ('user','admin','staff','opsmanager')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bản cũ không có hai vai trò này; không hạ về user thì ràng buộc cũ không thêm lại được.
            migrationBuilder.Sql("UPDATE users SET role = 'user' WHERE role IN ('staff','opsmanager');");

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
