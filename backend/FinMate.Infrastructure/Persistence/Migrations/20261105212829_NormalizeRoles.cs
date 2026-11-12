using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tách vai trò khỏi cột chữ <c>users.role</c> thành bảng <c>roles</c> + <c>user_roles</c> (mỗi người đúng một hàng), và đưa <c>role_permissions.role</c> thành khóa ngoại.
    /// Thứ tự cố ý: tạo bảng → CHÉP dữ liệu → mới bỏ cột. Hàng <c>roles.id</c> trùng giá trị enum <c>UserRole</c> (User=0, Admin=1, Staff=2, OpsManager=3).
    /// Vai trò lạ (ngoài 4 giá trị của <c>chk_users_role</c>) làm migration lỗi to chứ không âm thầm biến thành User.
    /// </summary>
    public partial class NormalizeRoles : Migration
    {
        private const string RoleToId = "CASE {0} WHEN 'user' THEN 0 WHEN 'admin' THEN 1 WHEN 'staff' THEN 2 WHEN 'opsmanager' THEN 3 END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_roles_code",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO roles (id, code, name, description, is_system, created_at) VALUES
 (0, 'user',       'Người dùng',        'Khách hàng dùng ứng dụng, không có quyền nội bộ.', true, now()),
 (1, 'admin',      'Quản trị viên',     'Toàn quyền, gồm trang /admin/*.', true, now()),
 (2, 'staff',      'Nhân viên hỗ trợ',  'Xử lý ticket và hỗ trợ khách hàng.', true, now()),
 (3, 'opsmanager', 'Quản lý vận hành',  'Điều phối ticket, duyệt việc nhạy cảm, theo dõi hệ thống.', true, now());");

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_roles_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_user_roles_assigned_by_user_id", table: "user_roles", column: "assigned_by_user_id");
            migrationBuilder.CreateIndex(name: "IX_user_roles_role_id", table: "user_roles", column: "role_id");

            // Chép vai trò hiện có: mỗi user đúng một hàng.
            migrationBuilder.Sql($"INSERT INTO user_roles (user_id, role_id, assigned_at) SELECT id, {string.Format(RoleToId, "role")}, now() FROM users;");

            // role_permissions.role (chữ) → role_id (số, khóa ngoại tới roles).
            migrationBuilder.Sql("ALTER TABLE role_permissions ADD COLUMN role_id integer;");
            migrationBuilder.Sql($"UPDATE role_permissions SET role_id = {string.Format(RoleToId, "role")};");
            migrationBuilder.Sql("ALTER TABLE role_permissions ALTER COLUMN role_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE role_permissions DROP CONSTRAINT \"PK_role_permissions\";");
            migrationBuilder.Sql("ALTER TABLE role_permissions DROP COLUMN role;");
            migrationBuilder.Sql("ALTER TABLE role_permissions ADD CONSTRAINT \"PK_role_permissions\" PRIMARY KEY (role_id);");
            migrationBuilder.AddForeignKey(
                name: "FK_role_permissions_roles_role_id",
                table: "role_permissions",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Dữ liệu đã nằm ở user_roles: bỏ nguồn thứ hai.
            migrationBuilder.DropCheckConstraint(name: "chk_users_role", table: "users");
            migrationBuilder.DropColumn(name: "role", table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "user");

            migrationBuilder.Sql("UPDATE users u SET role = r.code FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = u.id;");
            migrationBuilder.AddCheckConstraint(
                name: "chk_users_role",
                table: "users",
                sql: "role IN ('user','admin','staff','opsmanager')");

            migrationBuilder.DropForeignKey(name: "FK_role_permissions_roles_role_id", table: "role_permissions");
            migrationBuilder.Sql("ALTER TABLE role_permissions ADD COLUMN role character varying(20);");
            migrationBuilder.Sql("UPDATE role_permissions rp SET role = r.code FROM roles r WHERE r.id = rp.role_id;");
            migrationBuilder.Sql("ALTER TABLE role_permissions ALTER COLUMN role SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE role_permissions DROP CONSTRAINT \"PK_role_permissions\";");
            migrationBuilder.Sql("ALTER TABLE role_permissions DROP COLUMN role_id;");
            migrationBuilder.Sql("ALTER TABLE role_permissions ADD CONSTRAINT \"PK_role_permissions\" PRIMARY KEY (role);");

            migrationBuilder.DropTable(name: "user_roles");
            migrationBuilder.DropTable(name: "roles");
        }
    }
}
