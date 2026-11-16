using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_merge_snapshots_kind",
                table: "transaction_merge_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddColumn<string>(
                name: "group_fund_direction",
                table: "transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_fund_entry_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    balance_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    negative_episode = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_funds", x => x.id);
                    table.CheckConstraint("chk_group_funds_archived_settled", "archived_at IS NULL OR balance_cents = 0");
                    table.CheckConstraint("chk_group_funds_name", "char_length(name) BETWEEN 1 AND 100");
                    table.CheckConstraint("chk_group_funds_negative_episode", "negative_episode >= 0");
                    table.ForeignKey(
                        name: "FK_group_funds_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_funds_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_fund_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    transacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    merchant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "created"),
                    origin_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_fund_entries", x => x.id);
                    table.CheckConstraint("chk_group_fund_entries_amount", "(kind = 'adjustment' AND amount_cents <> 0 AND note IS NOT NULL) OR (kind <> 'adjustment' AND amount_cents > 0)");
                    table.CheckConstraint("chk_group_fund_entries_expense_category", "kind <> 'expense' OR category_id IS NOT NULL OR group_category_id IS NOT NULL");
                    table.CheckConstraint("chk_group_fund_entries_expense_fields", "kind = 'expense' OR (category_id IS NULL AND group_category_id IS NULL AND merchant_name IS NULL)");
                    table.CheckConstraint("chk_group_fund_entries_kind", "kind IN ('contribution','expense','withdrawal','adjustment')");
                    table.CheckConstraint("chk_group_fund_entries_origin", "origin IN ('created','converted')");
                    table.CheckConstraint("chk_group_fund_entries_origin_category", "origin_category_id IS NULL OR origin = 'converted'");
                    table.CheckConstraint("chk_group_fund_entries_origin_kind", "origin = 'created' OR kind = 'expense'");
                    table.ForeignKey(
                        name: "FK_group_fund_entries_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_categories_origin_category_id",
                        column: x => x.origin_category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_group_categories_group_category_id",
                        column: x => x.group_category_id,
                        principalTable: "group_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_group_funds_fund_id",
                        column: x => x.fund_id,
                        principalTable: "group_funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_users_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_fund_entries_users_member_user_id",
                        column: x => x.member_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "uq_transactions_group_fund_entry_id",
                table: "transactions",
                column: "group_fund_entry_id",
                unique: true,
                filter: "group_fund_entry_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_group_fund_link",
                table: "transactions",
                sql: "(group_fund_entry_id IS NULL) = (group_fund_direction IS NULL) AND (group_fund_direction IS NULL OR group_fund_direction IN ('out','in')) AND (group_fund_entry_id IS NULL OR (transaction_type = 'transfer' AND counter_account_id IS NULL AND group_id IS NULL AND status = 'confirmed'))");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions",
                sql: "(transaction_type = 'transfer' AND counter_account_id IS NOT NULL AND counter_account_id <> financial_account_id AND category_id IS NULL) OR (transaction_type = 'transfer' AND counter_account_id IS NULL AND category_id IS NULL AND group_fund_entry_id IS NOT NULL) OR (transaction_type <> 'transfer' AND counter_account_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_merge_snapshots_kind",
                table: "transaction_merge_snapshots",
                sql: "kind IN ('duplicate_merge','transfer_pair','transfer_convert','group_fund_expense')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed','fund_created','fund_updated','fund_archived','fund_contributed','fund_withdrawn','fund_entry_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed')");

            migrationBuilder.CreateIndex(
                name: "IX_group_fund_entries_category_id",
                table: "group_fund_entries",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_fund_entries_deleted_by_user_id",
                table: "group_fund_entries",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_fund_entries_group_category_id",
                table: "group_fund_entries",
                column: "group_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_fund_entries_member_user_id",
                table: "group_fund_entries",
                column: "member_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_fund_entries_origin_category_id",
                table: "group_fund_entries",
                column: "origin_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_fund_entries_fund_transacted",
                table: "group_fund_entries",
                columns: new[] { "fund_id", "transacted_at", "id" },
                descending: new[] { false, true, true },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_group_fund_entries_group_kind_transacted",
                table: "group_fund_entries",
                columns: new[] { "group_id", "kind", "transacted_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_group_fund_entries_client_request",
                table: "group_fund_entries",
                columns: new[] { "fund_id", "member_user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_group_funds_created_by_user_id",
                table: "group_funds",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_funds_group_active",
                table: "group_funds",
                column: "group_id",
                filter: "archived_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_group_fund_entries_group_fund_entry_id",
                table: "transactions",
                column: "group_fund_entry_id",
                principalTable: "group_fund_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_group_fund_entries_group_fund_entry_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "group_fund_entries");

            migrationBuilder.DropTable(
                name: "group_funds");

            migrationBuilder.DropIndex(
                name: "uq_transactions_group_fund_entry_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_group_fund_link",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_merge_snapshots_kind",
                table: "transaction_merge_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.DropColumn(
                name: "group_fund_direction",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "group_fund_entry_id",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions",
                sql: "(transaction_type = 'transfer' AND counter_account_id IS NOT NULL AND counter_account_id <> financial_account_id AND category_id IS NULL) OR (transaction_type <> 'transfer' AND counter_account_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_merge_snapshots_kind",
                table: "transaction_merge_snapshots",
                sql: "kind IN ('duplicate_merge','transfer_pair','transfer_convert')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health')");
        }
    }
}
