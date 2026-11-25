using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sau khi gộp dev/tho vào dev/tuan: AddGoldPricesAndGoalMarketTopic thêm <c>goal_market_shift</c> còn AddLoanReminders (chạy sau) dựng lại CHECK
    /// chỉ có <c>loan_due</c>, nên chạy tuần tự sẽ làm mất <c>goal_market_shift</c>. Đặt lại CHECK chứa cả hai.
    /// </summary>
    public partial class AlertKindsUnionAfterMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res','goal_market_shift','loan_due')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CHECK trước đó không có goal_market_shift: bỏ các cảnh báo đó trước khi đặt lại nó.
            migrationBuilder.Sql("DELETE FROM forecast_alerts WHERE kind = 'goal_market_shift';");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res','loan_due')");
        }
    }
}
