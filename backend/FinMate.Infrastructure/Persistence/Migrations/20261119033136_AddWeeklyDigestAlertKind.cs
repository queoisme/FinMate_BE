using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyDigestAlertKind : Migration
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
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder')");
        }
    }
}
