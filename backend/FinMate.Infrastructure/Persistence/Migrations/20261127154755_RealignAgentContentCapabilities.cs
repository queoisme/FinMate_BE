using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gộp hai nhánh cùng dùng bit 18-19: nhánh dev đặt ManageAgentContent = 1 &lt;&lt; 18, nhánh main đặt ViewProductKpi = 1 &lt;&lt; 18 và
    /// MaintainCatalog = 1 &lt;&lt; 19. Sau khi gộp ManageAgentContent là 1 &lt;&lt; 23 (8388608), nên môi trường đã chạy
    /// GrantAgentContentCapabilities còn dư bit 18 ở Staff (nay nghĩa là ViewProductKpi, chỉ Manager trở lên được xem).
    /// Staff: bỏ bit 18, thêm bit 23. OpsManager: thêm bit 23 (đã có bit 18 hợp lệ). Admin tính từ enum, không có hàng.
    /// Số viết cứng để migration không đổi nghĩa nếu enum đổi về sau.
    /// </summary>
    public partial class RealignAgentContentCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = (capabilities & ~262144) | 8388608, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 8388608, updated_at = now() WHERE role_id = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = (capabilities & ~8388608) | 262144, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~8388608, updated_at = now() WHERE role_id = 3;");
        }
    }
}
