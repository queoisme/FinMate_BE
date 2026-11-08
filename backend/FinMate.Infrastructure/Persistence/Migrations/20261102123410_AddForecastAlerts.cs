using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "forecast_alerts_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "low_balance_threshold_cents",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "quiet_hours_end_minute",
                table: "users",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "quiet_hours_start_minute",
                table: "users",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "forecast_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    feedback = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    feedback_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_alerts", x => x.id);
                    table.CheckConstraint("chk_forecast_alerts_feedback", "feedback IS NULL OR feedback = 'inaccurate'");
                    table.CheckConstraint("chk_forecast_alerts_kind", "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change')");
                    table.CheckConstraint("chk_forecast_alerts_sent_at", "(status = 'sent') = (sent_at IS NOT NULL)");
                    table.CheckConstraint("chk_forecast_alerts_status", "status IN ('pending','sent')");
                    table.ForeignKey(
                        name: "FK_forecast_alerts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_low_balance_threshold",
                table: "users",
                sql: "low_balance_threshold_cents IS NULL OR low_balance_threshold_cents >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_quiet_hours",
                table: "users",
                sql: "(quiet_hours_start_minute IS NULL AND quiet_hours_end_minute IS NULL) OR (quiet_hours_start_minute BETWEEN 0 AND 1439 AND quiet_hours_end_minute BETWEEN 0 AND 1439)");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_alerts_pending_due",
                table: "forecast_alerts",
                column: "scheduled_for",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_alerts_user_created",
                table: "forecast_alerts",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_forecast_alerts_user_kind_dedup",
                table: "forecast_alerts",
                columns: new[] { "user_id", "kind", "dedup_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_alerts");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_low_balance_threshold",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_quiet_hours",
                table: "users");

            migrationBuilder.DropColumn(
                name: "forecast_alerts_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "low_balance_threshold_cents",
                table: "users");

            migrationBuilder.DropColumn(
                name: "quiet_hours_end_minute",
                table: "users");

            migrationBuilder.DropColumn(
                name: "quiet_hours_start_minute",
                table: "users");
        }
    }
}
