using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListenerHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events");

            migrationBuilder.DropIndex(
                name: "IX_notification_logs_financial_account_id",
                table: "notification_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddColumn<bool>(
                name: "listener_health_alerts_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "device_listener_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_granted = table.Column<bool>(type: "boolean", nullable: false),
                    listener_connected = table.Column<bool>(type: "boolean", nullable: false),
                    selected_packages = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    app_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    permission_lost_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_listener_statuses", x => x.id);
                    table.CheckConstraint("chk_device_listener_statuses_packages", "jsonb_typeof(selected_packages) = 'array'");
                    table.ForeignKey(
                        name: "FK_device_listener_statuses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listener_upload_gap_daily",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    app_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listener_upload_gap_daily", x => new { x.day, x.app_version });
                    table.CheckConstraint("chk_listener_upload_gap_daily_count", "count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "device_listener_daily",
                columns: table => new
                {
                    status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    any_permission_denied = table.Column<bool>(type: "boolean", nullable: false),
                    last_permission_granted = table.Column<bool>(type: "boolean", nullable: false),
                    last_listener_connected = table.Column<bool>(type: "boolean", nullable: false),
                    last_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    report_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_listener_daily", x => new { x.status_id, x.day });
                    table.CheckConstraint("chk_device_listener_daily_report_count", "report_count >= 1");
                    table.ForeignKey(
                        name: "FK_device_listener_daily_device_listener_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "device_listener_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_listener_daily_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents",
                sql: "source IN ('health','app_error','job','provider','payment','notification_format')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events",
                sql: "source IN ('health','app_error','job','provider','payment','notification_format')");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_account_received",
                table: "notification_logs",
                columns: new[] { "financial_account_id", "received_at" },
                filter: "financial_account_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health')");

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_created_at",
                table: "ai_results",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_listener_daily_day",
                table: "device_listener_daily",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_device_listener_daily_user_day",
                table: "device_listener_daily",
                columns: new[] { "user_id", "day" });

            migrationBuilder.CreateIndex(
                name: "uq_device_listener_statuses_user_installation",
                table: "device_listener_statuses",
                columns: new[] { "user_id", "installation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_listener_daily");

            migrationBuilder.DropTable(
                name: "listener_upload_gap_daily");

            migrationBuilder.DropTable(
                name: "device_listener_statuses");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events");

            migrationBuilder.DropIndex(
                name: "ix_notification_logs_account_received",
                table: "notification_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.DropIndex(
                name: "ix_ai_results_created_at",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "listener_health_alerts_enabled",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents",
                sql: "source IN ('health','app_error','job','provider','payment')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events",
                sql: "source IN ('health','app_error','job','provider','payment')");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_financial_account_id",
                table: "notification_logs",
                column: "financial_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change')");
        }
    }
}
