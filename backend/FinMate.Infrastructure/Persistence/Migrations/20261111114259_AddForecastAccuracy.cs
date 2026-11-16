using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_date = table.Column<DateOnly>(type: "date", nullable: false),
                    horizon_days = table.Column<int>(type: "integer", nullable: false),
                    horizon_date = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    config_version = table.Column<int>(type: "integer", nullable: false),
                    start_balance_cents = table.Column<long>(type: "bigint", nullable: false),
                    predicted_cents = table.Column<long>(type: "bigint", nullable: false),
                    conservative_cents = table.Column<long>(type: "bigint", nullable: false),
                    optimistic_cents = table.Column<long>(type: "bigint", nullable: false),
                    recurring_only_cents = table.Column<long>(type: "bigint", nullable: false),
                    confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    actual_cents = table.Column<long>(type: "bigint", nullable: true),
                    actual_filled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_snapshots", x => x.id);
                    table.CheckConstraint("chk_forecast_snapshots_actual", "(actual_cents IS NULL) = (actual_filled_at IS NULL)");
                    table.CheckConstraint("chk_forecast_snapshots_confidence", "confidence IN ('low','normal')");
                    table.CheckConstraint("chk_forecast_snapshots_horizon", "horizon_days > 0 AND horizon_date > origin_date");
                    table.ForeignKey(
                        name: "FK_forecast_snapshots_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forecast_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cause = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    labeled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    labeled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_feedback", x => x.id);
                    table.CheckConstraint("chk_forecast_feedback_cause", "cause IS NULL OR cause IN ('data_missing','recurring_misread','one_off','model_issue','other')");
                    table.CheckConstraint("chk_forecast_feedback_labeled", "(status = 'labeled') = (cause IS NOT NULL AND labeled_at IS NOT NULL)");
                    table.CheckConstraint("chk_forecast_feedback_reason", "reason IN ('balance_wrong','missing_bill','wrong_bill','unusual_spending','other')");
                    table.CheckConstraint("chk_forecast_feedback_status", "status IN ('new','labeled')");
                    table.ForeignKey(
                        name: "FK_forecast_feedback_forecast_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "forecast_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_forecast_feedback_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forecast_feedback_snapshot_id",
                table: "forecast_feedback",
                column: "snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_feedback_status_created",
                table: "forecast_feedback",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_forecast_feedback_user_snapshot_new",
                table: "forecast_feedback",
                columns: new[] { "user_id", "snapshot_id" },
                filter: "status = 'new'");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_awaiting_actual",
                table: "forecast_snapshots",
                column: "horizon_date",
                filter: "actual_cents IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_filled_origin",
                table: "forecast_snapshots",
                column: "origin_date",
                filter: "actual_cents IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_forecast_snapshots_user_origin_horizon",
                table: "forecast_snapshots",
                columns: new[] { "user_id", "origin_date", "horizon_days" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_feedback");

            migrationBuilder.DropTable(
                name: "forecast_snapshots");
        }
    }
}
