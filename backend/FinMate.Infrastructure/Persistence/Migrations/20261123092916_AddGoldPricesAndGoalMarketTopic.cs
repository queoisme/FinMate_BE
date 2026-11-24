using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoldPricesAndGoalMarketTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "market_baseline_at",
                table: "saving_goals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "market_baseline_cents",
                table: "saving_goals",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "market_topic",
                table: "saving_goals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gold_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    gold_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    buy_cents = table.Column<long>(type: "bigint", nullable: false),
                    sell_cents = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_domains = table.Column<string[]>(type: "text[]", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    alerts_processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gold_prices", x => x.id);
                    table.CheckConstraint("chk_gold_prices_manual_note", "origin <> 'manual' OR note IS NOT NULL");
                    table.CheckConstraint("chk_gold_prices_origin", "origin IN ('sources','manual')");
                    table.CheckConstraint("chk_gold_prices_prices", "buy_cents > 0 AND sell_cents >= buy_cents");
                    table.ForeignKey(
                        name: "FK_gold_prices_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_market",
                table: "saving_goals",
                sql: "(market_topic IS NULL AND market_baseline_cents IS NULL AND market_baseline_at IS NULL) OR (market_topic IS NOT NULL AND market_baseline_cents > 0 AND market_baseline_at IS NOT NULL AND user_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res','goal_market_shift')");

            migrationBuilder.CreateIndex(
                name: "IX_gold_prices_created_by_user_id",
                table: "gold_prices",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_gold_prices_key_time",
                table: "gold_prices",
                columns: new[] { "gold_key", "first_seen_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gold_prices");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_market",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            // Bản cũ không có loại cảnh báo này.
            migrationBuilder.Sql("DELETE FROM forecast_alerts WHERE kind = 'goal_market_shift';");

            migrationBuilder.DropColumn(
                name: "market_baseline_at",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "market_baseline_cents",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "market_topic",
                table: "saving_goals");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res')");
        }
    }
}
