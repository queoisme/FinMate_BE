using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNotificationPushData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_user_notifications_type",
                table: "user_notifications");

            migrationBuilder.AddColumn<string>(
                name: "data",
                table: "user_notifications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_user_notifications_type",
                table: "user_notifications",
                sql: "type IN ('budget_threshold','balance_change','anomaly_detected','recurring_bill_due','forecast_ready','goal_milestone','transaction_review','forecast_alert','general')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_user_notifications_type",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "data",
                table: "user_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "chk_user_notifications_type",
                table: "user_notifications",
                sql: "type IN ('budget_threshold','balance_change','anomaly_detected','recurring_bill_due','forecast_ready','goal_milestone')");
        }
    }
}
