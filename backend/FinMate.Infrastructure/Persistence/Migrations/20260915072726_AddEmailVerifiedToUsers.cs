using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerifiedToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Coi mọi tài khoản ĐÃ CÓ là đã xác minh. Không backfill thì bật
            // REQUIRE_EMAIL_VERIFICATION lên là khoá toàn bộ người dùng hiện tại ra ngoài —
            // họ đăng ký từ trước khi tính năng này tồn tại nên không có cách nào xác minh
            // ngược lại. Chỉ ràng buộc người đăng ký TỪ ĐÂY TRỞ ĐI.
            migrationBuilder.Sql(
                "UPDATE users SET email_verified_at = NOW() WHERE email_verified_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "users");
        }
    }
}
