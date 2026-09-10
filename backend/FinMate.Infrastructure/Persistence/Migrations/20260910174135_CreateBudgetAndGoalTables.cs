using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBudgetAndGoalTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "saving_goals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deadline_notified_at",
                table: "saving_goals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    limit_cents = table.Column<long>(type: "bigint", nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.id);
                    table.CheckConstraint("chk_budgets_limit_cents", "limit_cents > 0");
                    table.CheckConstraint("chk_budgets_period_type", "period_type IN ('monthly')");
                    table.ForeignKey(
                        name: "FK_budgets_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_budgets_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_contributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    saving_goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contributed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_contributions", x => x.id);
                    table.CheckConstraint("chk_goal_contributions_amount_cents", "amount_cents > 0");
                    table.ForeignKey(
                        name: "FK_goal_contributions_saving_goals_saving_goal_id",
                        column: x => x.saving_goal_id,
                        principalTable: "saving_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    limit_cents = table.Column<long>(type: "bigint", nullable: false),
                    spent_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    alert_80_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    alert_100_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_periods", x => x.id);
                    table.ForeignKey(
                        name: "FK_budget_periods_budgets_budget_id",
                        column: x => x.budget_id,
                        principalTable: "budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals",
                sql: "status IN ('active','completed','cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_target_cents",
                table: "saving_goals",
                sql: "target_cents > 0");

            migrationBuilder.CreateIndex(
                name: "idx_budget_periods_period_end",
                table: "budget_periods",
                column: "period_end");

            migrationBuilder.CreateIndex(
                name: "uq_budget_periods_budget_start",
                table: "budget_periods",
                columns: new[] { "budget_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budgets_category_id",
                table: "budgets",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "uq_budgets_user_category",
                table: "budgets",
                columns: new[] { "user_id", "category_id", "period_type" },
                unique: true,
                filter: "category_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_budgets_user_total",
                table: "budgets",
                columns: new[] { "user_id", "period_type" },
                unique: true,
                filter: "category_id IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_goal_contributions_goal_contributed_at",
                table: "goal_contributions",
                columns: new[] { "saving_goal_id", "contributed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_periods");

            migrationBuilder.DropTable(
                name: "goal_contributions");

            migrationBuilder.DropTable(
                name: "budgets");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_target_cents",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "deadline_notified_at",
                table: "saving_goals");
        }
    }
}
