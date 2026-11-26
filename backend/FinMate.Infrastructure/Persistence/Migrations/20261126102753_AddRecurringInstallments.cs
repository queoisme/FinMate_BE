using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_recurring_items_user_direction_counterparty",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_status",
                table: "recurring_items");

            migrationBuilder.AddColumn<short>(
                name: "anchor_day",
                table: "recurring_items",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "recurring_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "recurring_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "prepaid_occurrences",
                table: "recurring_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "settled_early",
                table: "recurring_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "total_occurrences",
                table: "recurring_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_item_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurring_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_item_payments", x => x.id);
                    table.CheckConstraint("chk_recurring_item_payments_kind", "kind IN ('matched','manual','settlement')");
                    table.ForeignKey(
                        name: "FK_recurring_item_payments_recurring_items_recurring_item_id",
                        column: x => x.recurring_item_id,
                        principalTable: "recurring_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_item_payments_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_item_payments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_recurring_items_user_direction_counterparty",
                table: "recurring_items",
                columns: new[] { "user_id", "direction", "counterparty_key" },
                unique: true,
                filter: "counterparty_key IS NOT NULL AND deleted_at IS NULL AND status <> 'completed'");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_anchor_day",
                table: "recurring_items",
                sql: "anchor_day IS NULL OR (anchor_day BETWEEN 1 AND 31 AND cadence = 'monthly')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_completed_at",
                table: "recurring_items",
                sql: "(status = 'completed') = (completed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_one_end",
                table: "recurring_items",
                sql: "total_occurrences IS NULL OR end_date IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_prepaid",
                table: "recurring_items",
                sql: "prepaid_occurrences >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_settled_early",
                table: "recurring_items",
                sql: "NOT settled_early OR status = 'completed'");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_status",
                table: "recurring_items",
                sql: "status IN ('suggested','active','paused','rejected','cancelled','completed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_total_occurrences",
                table: "recurring_items",
                sql: "total_occurrences IS NULL OR total_occurrences BETWEEN 1 AND 600");

            // S2: suy anchor_day cho khoản tháng. Có lần trả gần nhất: cả ngày đến hạn và ngày trả đều là cuối tháng → 31, ngược lại lấy ngày lớn hơn
            // (khoản ngày 30 đến hạn 30/11 có lần trả 30/10 → 30). Không có lần trả: ngày đến hạn là cuối tháng → 31, còn lại → ngày của nó (D8).
            // Không khôi phục ngày gốc của khoản đã trôi (BR-INS-05). Ngày trả lấy theo giờ VN (UTC+7).
            migrationBuilder.Sql("""
                WITH d AS (
                    SELECT id,
                           next_due_date AS due,
                           ((last_seen_at AT TIME ZONE 'UTC') + interval '7 hours')::date AS seen
                    FROM recurring_items
                    WHERE cadence = 'monthly'
                )
                UPDATE recurring_items r
                SET anchor_day = CASE
                    WHEN d.seen IS NULL THEN
                        CASE WHEN d.due = (date_trunc('month', d.due) + interval '1 month - 1 day')::date THEN 31
                             ELSE extract(day FROM d.due)::smallint END
                    WHEN d.due = (date_trunc('month', d.due) + interval '1 month - 1 day')::date
                         AND d.seen = (date_trunc('month', d.seen) + interval '1 month - 1 day')::date THEN 31
                    ELSE greatest(extract(day FROM d.due), extract(day FROM d.seen))::smallint
                END
                FROM d
                WHERE r.id = d.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_item_payments_user",
                table: "recurring_item_payments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_recurring_item_payments_item_due",
                table: "recurring_item_payments",
                columns: new[] { "recurring_item_id", "due_date" },
                unique: true,
                filter: "deleted_at IS NULL AND kind <> 'settlement'");

            migrationBuilder.CreateIndex(
                name: "uq_recurring_item_payments_transaction",
                table: "recurring_item_payments",
                column: "transaction_id",
                unique: true,
                filter: "transaction_id IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_item_payments");

            migrationBuilder.DropIndex(
                name: "uq_recurring_items_user_direction_counterparty",
                table: "recurring_items");

            // Lược đồ cũ không có trạng thái completed và để dòng đó giữ chỗ trong index duy nhất: về cancelled (vẫn chặn đề xuất lại);
            // dòng nào trùng người nhận với một khoản còn sống khác thì xoá mềm để index cũ dựng lại được.
            migrationBuilder.Sql("""
                UPDATE recurring_items r
                SET deleted_at = now()
                WHERE r.status = 'completed' AND r.deleted_at IS NULL AND r.counterparty_key IS NOT NULL
                  AND EXISTS (SELECT 1 FROM recurring_items o
                              WHERE o.id <> r.id AND o.user_id = r.user_id AND o.direction = r.direction
                                AND o.counterparty_key = r.counterparty_key AND o.deleted_at IS NULL AND o.status <> 'completed');
                UPDATE recurring_items r
                SET deleted_at = now()
                WHERE r.status = 'completed' AND r.deleted_at IS NULL AND r.counterparty_key IS NOT NULL
                  AND EXISTS (SELECT 1 FROM recurring_items o
                              WHERE o.id < r.id AND o.user_id = r.user_id AND o.direction = r.direction
                                AND o.counterparty_key = r.counterparty_key AND o.deleted_at IS NULL AND o.status = 'completed');
                UPDATE recurring_items SET status = 'cancelled', completed_at = NULL, settled_early = false WHERE status = 'completed';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_anchor_day",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_completed_at",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_one_end",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_prepaid",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_settled_early",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_status",
                table: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_recurring_items_total_occurrences",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "anchor_day",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "prepaid_occurrences",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "settled_early",
                table: "recurring_items");

            migrationBuilder.DropColumn(
                name: "total_occurrences",
                table: "recurring_items");

            migrationBuilder.CreateIndex(
                name: "uq_recurring_items_user_direction_counterparty",
                table: "recurring_items",
                columns: new[] { "user_id", "direction", "counterparty_key" },
                unique: true,
                filter: "counterparty_key IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_recurring_items_status",
                table: "recurring_items",
                sql: "status IN ('suggested','active','paused','rejected','cancelled')");
        }
    }
}
