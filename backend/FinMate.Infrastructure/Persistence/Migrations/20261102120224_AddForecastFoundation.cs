using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "forecast_reserve_cents",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "exclude_from_forecast",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "forecast_exclusion_reason",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "include_in_forecast",
                table: "financial_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "recurring_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    counterparty_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    cadence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_items", x => x.id);
                    table.CheckConstraint("chk_recurring_items_amount", "amount_cents > 0");
                    table.CheckConstraint("chk_recurring_items_cadence", "cadence IN ('weekly','monthly','yearly')");
                    table.CheckConstraint("chk_recurring_items_direction", "direction IN ('inflow','outflow')");
                    table.CheckConstraint("chk_recurring_items_occurrences", "occurrences >= 0");
                    table.CheckConstraint("chk_recurring_items_source", "source IN ('detected','manual')");
                    table.CheckConstraint("chk_recurring_items_status", "status IN ('suggested','active','paused','rejected','cancelled')");
                    table.ForeignKey(
                        name: "FK_recurring_items_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recurring_items_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_recurring_items_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_forecast_reserve",
                table: "users",
                sql: "forecast_reserve_cents IS NULL OR forecast_reserve_cents >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_forecast_exclusion",
                table: "transactions",
                sql: "(exclude_from_forecast = false AND forecast_exclusion_reason IS NULL) OR (exclude_from_forecast = true AND forecast_exclusion_reason IN ('one_off','user_marked'))");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_items_category_id",
                table: "recurring_items",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_items_financial_account_id",
                table: "recurring_items",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_items_user_status",
                table: "recurring_items",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_recurring_items_user_direction_counterparty",
                table: "recurring_items",
                columns: new[] { "user_id", "direction", "counterparty_key" },
                unique: true,
                filter: "counterparty_key IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_items");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_forecast_reserve",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_forecast_exclusion",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "forecast_reserve_cents",
                table: "users");

            migrationBuilder.DropColumn(
                name: "exclude_from_forecast",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "forecast_exclusion_reason",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "include_in_forecast",
                table: "financial_accounts");
        }
    }
}
