using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Pentest 2026-11-28 (#12): <c>AddCommunity</c> và <c>AddSubscriptions</c> thêm quyền mặc định nhưng không cập nhật các hàng
    /// <c>role_permissions</c> đã có, còn <c>RolePermissionSeeder</c> không đụng hàng đã tồn tại. Môi trường tạo trước 6/11 vì vậy thiếu:
    /// Staff (role_id 2) thiếu <c>ModerateCommunity</c> (1 &lt;&lt; 14 = 16384); OpsManager (role_id 3) thiếu <c>ModerateCommunity</c>,
    /// <c>MuteCommunityAuthors</c> (1 &lt;&lt; 15 = 32768) và <c>ManageSubscriptionPlans</c> (1 &lt;&lt; 16 = 65536), nên bị 403 âm thầm ở
    /// <c>/ops/community</c> và <c>/ops/subscriptions</c>. Cấp theo mặc định trong <c>OpsPermissions</c>; Admin tính từ enum, không có hàng.
    /// Idempotent (OR bit): hàng đã đủ bit không đổi. Nếu Admin đã chủ ý bỏ tick các quyền này ở trang Phân quyền thì migration cấp lại.
    /// Số viết cứng để migration không đổi nghĩa nếu enum đổi về sau; Down gỡ đúng các bit này.
    /// </summary>
    public partial class GrantCommunityAndSubscriptionCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 16384, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 114688, updated_at = now() WHERE role_id = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~16384, updated_at = now() WHERE role_id = 2;");
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~114688, updated_at = now() WHERE role_id = 3;");
        }
    }
}
