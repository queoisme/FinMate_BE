using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.DropColumn(
                name: "due_notified_at",
                table: "loans");

            migrationBuilder.AddColumn<bool>(
                name: "loan_reminders_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res','loan_due')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CHECK cũ không có loan_due: bỏ các nhắc khoản vay trước khi đặt lại nó.
            migrationBuilder.Sql("DELETE FROM forecast_alerts WHERE kind = 'loan_due';");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.DropColumn(
                name: "loan_reminders_enabled",
                table: "users");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_notified_at",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res')");
        }
    }
}
