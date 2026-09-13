using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceAndReceiptTransactionSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions",
                sql: "source IN ('notification','manual','voice','receipt')");
        }

        /// <summary>
        /// Thu hẹp lại sẽ THẤT BẠI nếu đã có giao dịch voice/receipt — và đó là hành vi đúng.
        /// Khác với migration 0005 phía AI Service (nơi downgrade xóa mẫu huấn luyện thu thập
        /// được), ở đây dữ liệu là giao dịch tiền bạc của người dùng; hỏng migration còn hơn
        /// âm thầm xóa chúng để constraint vừa vặn trở lại.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions",
                sql: "source IN ('notification','manual')");
        }
    }
}
