using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocr_result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    merchant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    category_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    needs_review = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_scans", x => x.id);
                    table.CheckConstraint("chk_receipt_scans_amount_positive", "amount_cents IS NULL OR amount_cents > 0");
                    table.CheckConstraint("chk_receipt_scans_ocr_result", "ocr_result IN ('success','no_amount','unreadable')");
                    table.ForeignKey(
                        name: "FK_receipt_scans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_receipt_scans_user_created",
                table: "receipt_scans",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_scans");
        }
    }
}
