using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetAlert70And90 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF tự đoán rename 80 -> 90; ĐÃ SỬA TAY thành 80 -> 70. Ngữ nghĩa dữ liệu cũ là
            // "đã gửi cảnh báo nhẹ khi chạm ngưỡng đầu tiên", nay ngưỡng đầu tiên là 70%.
            // Map sang 90 sẽ vừa nuốt mất cảnh báo 90% thật, vừa bắn lại cảnh báo 70% cho
            // chu kỳ đang ở 85% — user đã được cảnh báo rồi lại bị cảnh báo mức thấp hơn.
            migrationBuilder.RenameColumn(
                name: "alert_80_sent_at",
                table: "budget_periods",
                newName: "alert_70_sent_at");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "alert_90_sent_at",
                table: "budget_periods",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alert_90_sent_at",
                table: "budget_periods");

            migrationBuilder.RenameColumn(
                name: "alert_70_sent_at",
                table: "budget_periods",
                newName: "alert_80_sent_at");
        }
    }
}
