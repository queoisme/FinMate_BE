using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptScanItemsAndLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_scan_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    category_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    parent_position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_scan_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_receipt_scan_items_receipt_scans_scan_id",
                        column: x => x.scan_id,
                        principalTable: "receipt_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_scan_links",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_category_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_scan_links", x => x.transaction_id);
                    table.ForeignKey(
                        name: "FK_receipt_scan_links_receipt_scans_scan_id",
                        column: x => x.scan_id,
                        principalTable: "receipt_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_receipt_scan_links_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_receipt_scan_items_scan_position",
                table: "receipt_scan_items",
                columns: new[] { "scan_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_receipt_scan_links_scan_group",
                table: "receipt_scan_links",
                columns: new[] { "scan_id", "group_category_slug" },
                unique: true);

            // Chép liên kết cũ sang bảng nối TRƯỚC khi bỏ cột: nhóm = slug AI đoán của lần quét, "unknown" khi không có.
            migrationBuilder.Sql(@"
INSERT INTO receipt_scan_links (transaction_id, scan_id, group_category_slug)
SELECT transaction_id, id, COALESCE(category_slug, 'unknown')
FROM receipt_scans
WHERE transaction_id IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_receipt_scans_transactions_transaction_id",
                table: "receipt_scans");

            migrationBuilder.DropIndex(
                name: "uq_receipt_scans_transaction_id",
                table: "receipt_scans");

            migrationBuilder.DropColumn(
                name: "transaction_id",
                table: "receipt_scans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transaction_id",
                table: "receipt_scans",
                type: "uuid",
                nullable: true);

            // Khôi phục cột cũ từ bảng nối: mỗi lần quét giữ một giao dịch, giao dịch được tạo sớm nhất.
            migrationBuilder.Sql(@"
UPDATE receipt_scans s
SET transaction_id = first_link.transaction_id
FROM (
    SELECT DISTINCT ON (l.scan_id) l.scan_id, l.transaction_id
    FROM receipt_scan_links l
    JOIN transactions t ON t.id = l.transaction_id
    ORDER BY l.scan_id, t.created_at, t.id
) first_link
WHERE s.id = first_link.scan_id;");

            migrationBuilder.CreateIndex(
                name: "uq_receipt_scans_transaction_id",
                table: "receipt_scans",
                column: "transaction_id",
                unique: true,
                filter: "transaction_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_receipt_scans_transactions_transaction_id",
                table: "receipt_scans",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(
                name: "receipt_scan_items");

            migrationBuilder.DropTable(
                name: "receipt_scan_links");
        }
    }
}
