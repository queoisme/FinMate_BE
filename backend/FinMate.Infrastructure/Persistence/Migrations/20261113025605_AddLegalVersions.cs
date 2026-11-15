using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    version = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    terms_sections = table.Column<string>(type: "jsonb", nullable: false),
                    privacy_sections = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_versions", x => x.id);
                    table.CheckConstraint("chk_legal_versions_version_iff_published", "(status = 'published') = (version IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "uq_legal_versions_single_draft",
                table: "legal_versions",
                column: "status",
                unique: true,
                filter: "status = 'draft'");

            migrationBuilder.CreateIndex(
                name: "uq_legal_versions_version",
                table: "legal_versions",
                column: "version",
                unique: true);

            // Bản 4 đã xuất bản: nội dung tạm lấy nguyên văn từ màn đọc văn bản ở app, để người đã đồng ý bản 4 không bị hỏi lại.
            // Bản đầy đủ (Google Vision, nhóm, hỗ trợ) do Admin xuất bản thành bản 5 sau khi pháp lý duyệt chữ.
            // Tên thuộc tính viết hoa vì System.Text.Json giải mã phân biệt hoa thường.
            migrationBuilder.Sql("""
                INSERT INTO legal_versions (id, version, status, terms_sections, privacy_sections, created_by, updated_at, published_at)
                VALUES (
                    gen_random_uuid(), 4, 'published',
                    $json$[
                      {"Title":"1. Giới thiệu","Body":"FinMate là ứng dụng đồng hành quản lý tài chính cá nhân thông minh. Khi đăng ký và sử dụng dịch vụ, bạn đồng ý tuân thủ các điều khoản được nêu tại đây."},
                      {"Title":"2. Quyền & Nghĩa vụ","Body":"Người dùng có nghĩa vụ bảo vệ tài khoản của mình. FinMate cam kết cung cấp dịch vụ quản lý thu chi, ngân sách và phân tích chi tiêu chính xác theo dữ liệu bạn nhập."},
                      {"Title":"3. Dịch vụ AI & Phân tích","Body":"Các khuyến nghị tài chính từ AI mang tính chất tham khảo, hỗ trợ ra quyết định cá nhân. FinMate không chịu trách nhiệm pháp lý đối với các quyết định đầu tư sinh lời của người dùng."},
                      {"Title":"4. Chấm dứt dịch vụ","Body":"Bạn có quyền xuất toàn bộ dữ liệu và yêu cầu xóa vĩnh viễn tài khoản bất kỳ lúc nào thông qua phần Cài đặt của ứng dụng."}
                    ]$json$::jsonb,
                    $json$[
                      {"Title":"1. Cam kết Zero-Credential","Body":"FinMate tuân thủ nguyên tắc Zero-Credential. Chúng tôi KHÔNG BAO GIỜ yêu cầu hay lưu trữ mật khẩu đăng nhập tài khoản ngân hàng hoặc mã OTP ngân hàng của bạn."},
                      {"Title":"2. Dữ liệu thu thập","Body":"Chúng tôi chỉ lưu trữ các thông tin giao dịch, hạn mức ngân sách và mục tiêu mà bạn chủ động ghi chép hoặc đồng ý quét qua hóa đơn/biến động số dư."},
                      {"Title":"3. Mã hóa & Bảo mật","Body":"Mọi dữ liệu trao đổi giữa thiết bị và máy chủ đều được mã hóa bằng giao thức HTTPS/TLS 1.3 và lưu trữ trong cơ sở dữ liệu an toàn tuân thủ tiêu chuẩn quốc tế."},
                      {"Title":"4. Quyền của chủ thể dữ liệu","Body":"Bạn có toàn quyền truy xuất, chỉnh sửa, thu hồi quyền đào tạo AI hoặc yêu cầu xóa sạch toàn bộ lịch sử giao dịch của mình."}
                    ]$json$::jsonb,
                    '00000000-0000-0000-0000-000000000000', now(), now());
                """);

            // Quyền ManageLegalDocuments (1 << 17 = 131072) mặc định thuộc OpsManager (role_id 3). RolePermissionSeeder chỉ chèn hàng khi vai trò chưa có hàng,
            // nên môi trường đã chạy trước đó có hàng OpsManager thiếu bit mới: không cấp ở đây thì Manager nhận 403 âm thầm cho tới khi Admin tick tay.
            // Số viết cứng để migration không đổi nghĩa nếu enum đổi về sau.
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities | 131072, updated_at = now() WHERE role_id = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE role_permissions SET capabilities = capabilities & ~131072, updated_at = now() WHERE role_id = 3;");

            migrationBuilder.DropTable(
                name: "legal_versions");
        }
    }
}
