using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateReportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_spent_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    total_income_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    transaction_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    top_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    top_category_spent_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_summaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_summaries_categories_top_category_id",
                        column: x => x.top_category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_daily_summaries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spending_insights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    insight_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spending_insights", x => x.id);
                    table.CheckConstraint("chk_spending_insights_insight_type", "insight_type IN ('vs_last_month','recurring_detected','unusual_spending')");
                    table.ForeignKey(
                        name: "FK_spending_insights_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_spending_insights_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_summaries_top_category_id",
                table: "daily_summaries",
                column: "top_category_id");

            migrationBuilder.CreateIndex(
                name: "uq_daily_summaries_user_date",
                table: "daily_summaries",
                columns: new[] { "user_id", "summary_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spending_insights_category_id",
                table: "spending_insights",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "idx_spending_insights_user_created_at",
                table: "spending_insights",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_spending_insights_user_type_category_period",
                table: "spending_insights",
                columns: new[] { "user_id", "insight_type", "category_id", "period_start" },
                unique: true,
                filter: "category_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_spending_insights_user_type_period",
                table: "spending_insights",
                columns: new[] { "user_id", "insight_type", "period_start" },
                unique: true,
                filter: "category_id IS NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_summaries");

            migrationBuilder.DropTable(
                name: "spending_insights");
        }
    }
}
