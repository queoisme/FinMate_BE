using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalDataExports : Migration
    {
        private const string PersonalDataCannedReplyId = "9a1f0e6c-5d2b-4c8e-a7f3-91b0d4e2c591";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_support_tickets_category",
                table: "support_tickets");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "support_tickets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "personal_data_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    format_version = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    download_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    download_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false),
                    last_downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    counts_toward_limit = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_data_exports", x => x.id);
                    table.CheckConstraint("ck_personal_data_exports_counters", "attempts >= 0 AND download_count >= 0 AND (size_bytes IS NULL OR size_bytes >= 0)");
                    table.CheckConstraint("ck_personal_data_exports_failed", "(status = 'failed') = (failure_code IS NOT NULL)");
                    table.CheckConstraint("ck_personal_data_exports_ready", "status <> 'ready' OR (ready_at IS NOT NULL AND expires_at IS NOT NULL)");
                    table.CheckConstraint("ck_personal_data_exports_status", "status IN ('pending','running','ready','failed','expired')");
                    table.ForeignKey(
                        name: "FK_personal_data_exports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "personal_data_export_archives",
                columns: table => new
                {
                    export_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_data_export_archives", x => x.export_id);
                    table.ForeignKey(
                        name: "FK_personal_data_export_archives_personal_data_exports_export_~",
                        column: x => x.export_id,
                        principalTable: "personal_data_exports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_support_tickets_category",
                table: "support_tickets",
                sql: "category IN ('security','account','transaction','receipt','notification','group','question','personal_data_request')");

            migrationBuilder.CreateIndex(
                name: "ix_personal_data_exports_status_expires",
                table: "personal_data_exports",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_personal_data_exports_user_requested",
                table: "personal_data_exports",
                columns: new[] { "user_id", "requested_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_personal_data_exports_user_active",
                table: "personal_data_exports",
                column: "user_id",
                unique: true,
                filter: "status IN ('pending','running')");

            // BR-EXP-14: câu trả lời mẫu cho ticket "Yêu cầu dữ liệu cá nhân". Id cố định để Down gỡ đúng dòng; nhân viên sửa chữ được ở /ops/canned-replies.
            migrationBuilder.Sql($"""
                INSERT INTO canned_replies (id, title, body, is_active, created_at, updated_at)
                VALUES ('{PersonalDataCannedReplyId}', 'Hướng dẫn tự tải dữ liệu cá nhân',
                        'Chào bạn, bạn có thể tự tải toàn bộ dữ liệu của mình ngay trong app: vào Cài đặt → Dữ liệu của tôi → Tải toàn bộ dữ liệu, nhập mã xác nhận được gửi tới email của bạn. Khi gói dữ liệu sẵn sàng, FinMate sẽ báo qua thông báo và email; bạn mở lại màn này để tải về. File được giữ 72 giờ. Nếu chỉ cần danh sách giao dịch, chọn Xuất giao dịch (CSV). Vì lý do bảo mật, nhân viên hỗ trợ không thể tạo hay tải dữ liệu thay bạn.',
                        true, now(), now())
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM canned_replies WHERE id = '{PersonalDataCannedReplyId}';");

            // Ràng buộc cũ không có loại mới: đưa các ticket đó về "question" trước khi siết lại.
            migrationBuilder.Sql("UPDATE support_tickets SET category = 'question' WHERE category = 'personal_data_request';");

            migrationBuilder.DropTable(
                name: "personal_data_export_archives");

            migrationBuilder.DropTable(
                name: "personal_data_exports");

            migrationBuilder.DropCheckConstraint(
                name: "chk_support_tickets_category",
                table: "support_tickets");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "support_tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddCheckConstraint(
                name: "chk_support_tickets_category",
                table: "support_tickets",
                sql: "category IN ('security','account','transaction','receipt','notification','group','question')");
        }
    }
}
