using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRasaMonitoringRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dịch vụ Rasa đã bị gỡ khỏi hệ thống: bản cũ để lại một dòng trạng thái luôn "unknown" (vẫn hiện trên trang giám sát)
            // và có thể còn sự cố đang mở không bao giờ tự đóng. Dòng trạng thái xóa hẳn; sự cố chỉ đóng, giữ lại làm lịch sử.
            migrationBuilder.Sql("DELETE FROM ops_service_status WHERE service = 'rasa';");
            migrationBuilder.Sql(
                "UPDATE ops_incidents SET status = 'resolved', resolved_at = now(), resolution_note = 'Dịch vụ đã được gỡ khỏi hệ thống' " +
                "WHERE service = 'rasa' AND status <> 'resolved';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không có gì để khôi phục: dòng trạng thái và sự cố của một dịch vụ đã gỡ không tạo lại được.
        }
    }
}
