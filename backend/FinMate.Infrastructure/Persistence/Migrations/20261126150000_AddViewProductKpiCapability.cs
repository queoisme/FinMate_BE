using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 94: quyền <c>ViewProductKpi</c> (1 &lt;&lt; 18 = 262144) mặc định thuộc OpsManager (role_id 3). Không đổi schema.
    /// <c>RolePermissionSeeder</c> chỉ chèn hàng khi vai trò chưa có hàng, nên môi trường đã chạy trước đó có hàng OpsManager thiếu bit mới:
    /// không cấp ở đây thì Manager nhận 403 âm thầm cho tới khi Admin tick tay. Số viết cứng để migration không đổi nghĩa nếu enum đổi về sau.
    /// </summary>
    public partial class AddViewProductKpiCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 262144, updated_at = now() WHERE role_id = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~262144, updated_at = now() WHERE role_id = 3;");
        }
    }
}
