using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAgentContentCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Quyền mới ManageAgentContent (1 << 18 = 262144) mặc định cho Staff (role_id 2) và OpsManager (3); ApproveAgentContent (1 << 19 = 524288) chỉ OpsManager.
            // RolePermissionSeeder không ghi đè hàng đã có, nên không cấp ở đây thì người đang soạn/duyệt tri thức bị 403 âm thầm cho tới khi Admin tick tay.
            // Số viết cứng để migration không đổi nghĩa nếu enum đổi về sau.
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 262144, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 786432, updated_at = now() WHERE role_id = 3;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~786432, updated_at = now() WHERE role_id IN (2, 3);");

        }
    }
}
