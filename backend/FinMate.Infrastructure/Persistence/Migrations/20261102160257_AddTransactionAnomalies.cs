using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAnomalies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.CreateTable(
                name: "transaction_anomalies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counterparty_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    typical_cents = table.Column<long>(type: "bigint", nullable: false),
                    ratio_percent = table.Column<int>(type: "integer", nullable: false),
                    resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_anomalies", x => x.id);
                    table.CheckConstraint("chk_transaction_anomalies_amounts", "amount_cents > 0 AND typical_cents > 0 AND ratio_percent > 0");
                    table.CheckConstraint("chk_transaction_anomalies_kind", "kind IN ('category_spike','merchant_spike','duplicate')");
                    table.CheckConstraint("chk_transaction_anomalies_resolution", "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious')");
                    table.CheckConstraint("chk_transaction_anomalies_resolved_at", "(resolution IS NULL) = (resolved_at IS NULL)");
                    table.CheckConstraint("chk_transaction_anomalies_severity", "severity IN ('normal','high')");
                    table.ForeignKey(
                        name: "FK_transaction_anomalies_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transaction_anomalies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly')");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_anomalies_user_created",
                table: "transaction_anomalies",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transaction_anomalies_user_normal",
                table: "transaction_anomalies",
                columns: new[] { "user_id", "category_id" },
                filter: "resolution = 'normal'");

            migrationBuilder.CreateIndex(
                name: "uq_transaction_anomalies_transaction",
                table: "transaction_anomalies",
                column: "transaction_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            // CHECK cũ không chấp nhận 'anomaly': bỏ các dòng đó trước, không thì rollback dừng giữa chừng.
            migrationBuilder.Sql("DELETE FROM forecast_alerts WHERE kind = 'anomaly';");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change')");
        }
    }
}
