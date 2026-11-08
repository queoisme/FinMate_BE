using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptScanOcrEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ocr_engine",
                table: "receipt_scans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_receipt_scans_ocr_engine",
                table: "receipt_scans",
                sql: "ocr_engine IS NULL OR ocr_engine IN ('google_vision','local')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_receipt_scans_ocr_engine",
                table: "receipt_scans");

            migrationBuilder.DropColumn(
                name: "ocr_engine",
                table: "receipt_scans");
        }
    }
}
