using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 95: bốn quyền mới. <c>MaintainCatalog</c> (1 &lt;&lt; 19 = 524288) và <c>DraftForecastConfig</c> (1 &lt;&lt; 20 = 1048576) cho Staff (role_id 2)
    /// và OpsManager (role_id 3); <c>ApproveForecastConfig</c> (1 &lt;&lt; 21 = 2097152) và <c>ApproveAgentContent</c> (1 &lt;&lt; 22 = 4194304) chỉ cho OpsManager.
    /// <c>RolePermissionSeeder</c> không đụng hàng đã có, nên không cấp ở đây thì môi trường cũ thiếu quyền âm thầm. Admin luôn đủ quyền (tính từ enum),
    /// không có hàng để cập nhật. Không đổi schema; số viết cứng để migration không đổi nghĩa nếu enum đổi về sau.
    /// </summary>
    public partial class AddCatalogAndForecastConfigCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 1572864, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 7864320, updated_at = now() WHERE role_id = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~1572864, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~7864320, updated_at = now() WHERE role_id = 3;");
        }
    }
}
