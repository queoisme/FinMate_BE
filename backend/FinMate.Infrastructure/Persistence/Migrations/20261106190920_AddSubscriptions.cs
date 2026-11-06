using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
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

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    monthly_price_cents = table.Column<long>(type: "bigint", nullable: true),
                    yearly_price_cents = table.Column<long>(type: "bigint", nullable: true),
                    is_default_free = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.id);
                    table.CheckConstraint("chk_subscription_plans_free_has_no_price", "NOT is_default_free OR (monthly_price_cents IS NULL AND yearly_price_cents IS NULL)");
                    table.CheckConstraint("chk_subscription_plans_monthly_price", "monthly_price_cents IS NULL OR monthly_price_cents > 0");
                    table.CheckConstraint("chk_subscription_plans_yearly_price", "yearly_price_cents IS NULL OR yearly_price_cents > 0");
                });

            migrationBuilder.CreateTable(
                name: "subscription_usage",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    used = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_usage", x => new { x.user_id, x.feature_key, x.month });
                    table.CheckConstraint("chk_subscription_usage_used", "used >= 0");
                    table.ForeignKey(
                        name: "FK_subscription_usage_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    plan_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    features = table.Column<string>(type: "jsonb", nullable: false),
                    cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sepay_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    review_alerted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_orders", x => x.id);
                    table.CheckConstraint("chk_subscription_orders_amount", "amount_cents > 0");
                    table.CheckConstraint("chk_subscription_orders_cycle", "cycle IN ('monthly','yearly')");
                    table.CheckConstraint("chk_subscription_orders_features", "jsonb_typeof(features) = 'array'");
                    table.CheckConstraint("chk_subscription_orders_status", "status IN ('pending','paid','cancelled','expired','voided','needs_review')");
                    table.ForeignKey(
                        name: "FK_subscription_orders_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan_features",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    monthly_limit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan_features", x => new { x.plan_id, x.feature_key });
                    table.CheckConstraint("chk_subscription_plan_features_limit", "monthly_limit IS NULL OR monthly_limit >= 0");
                    table.ForeignKey(
                        name: "FK_subscription_plan_features_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_subscription_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    plan_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    features = table.Column<string>(type: "jsonb", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_7d_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminded_1d_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_subscription_periods", x => x.id);
                    table.CheckConstraint("chk_user_subscription_periods_features", "jsonb_typeof(features) = 'array'");
                    table.CheckConstraint("chk_user_subscription_periods_range", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "FK_user_subscription_periods_subscription_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "subscription_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_subscription_periods_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents",
                sql: "source IN ('health','app_error','job','provider','payment')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events",
                sql: "source IN ('health','app_error','job','provider','payment')");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_orders_plan_id",
                table: "subscription_orders",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_orders_next_check",
                table: "subscription_orders",
                columns: new[] { "status", "next_check_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_orders_status",
                table: "subscription_orders",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_orders_user",
                table: "subscription_orders",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_subscription_orders_invoice",
                table: "subscription_orders",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_subscription_plans_code",
                table: "subscription_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_subscription_plans_default_free",
                table: "subscription_plans",
                column: "is_default_free",
                unique: true,
                filter: "is_default_free");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscription_periods_ends_at",
                table: "user_subscription_periods",
                column: "ends_at",
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_subscription_periods_user",
                table: "user_subscription_periods",
                columns: new[] { "user_id", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "uq_user_subscription_periods_order",
                table: "user_subscription_periods",
                column: "order_id",
                unique: true);

            // Gói người chưa mua được dùng. Hạn mức là số tạm, Manager chốt lại trước khi bật cờ features.subscriptions.
            // Literal cố ý nằm ở đây: migration phải giữ nguyên nghĩa dù code đổi.
            migrationBuilder.Sql(
                """
                INSERT INTO subscription_plans (id, code, name, description, is_default_free, is_active, sort_order, created_at, updated_at)
                VALUES ('5a1f0c52-6f0e-4f0b-9a55-0a6c1d7e0001', 'free', 'Miễn phí', 'Gói mặc định cho mọi tài khoản.', TRUE, TRUE, 0, now(), now());

                INSERT INTO subscription_plan_features (plan_id, feature_key, enabled, monthly_limit) VALUES
                    ('5a1f0c52-6f0e-4f0b-9a55-0a6c1d7e0001', 'ai_chat', TRUE, 20),
                    ('5a1f0c52-6f0e-4f0b-9a55-0a6c1d7e0001', 'voice_input', TRUE, 10),
                    ('5a1f0c52-6f0e-4f0b-9a55-0a6c1d7e0001', 'receipt_scan', TRUE, 10);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sự cố nguồn thanh toán không qua được ràng buộc cũ.
            migrationBuilder.Sql("DELETE FROM ops_incidents WHERE source = 'payment'");

            migrationBuilder.DropTable(
                name: "subscription_plan_features");

            migrationBuilder.DropTable(
                name: "subscription_usage");

            migrationBuilder.DropTable(
                name: "user_subscription_periods");

            migrationBuilder.DropTable(
                name: "subscription_orders");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_incidents_source",
                table: "ops_incidents",
                sql: "source IN ('health','app_error','job','provider')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ops_error_events_source",
                table: "ops_error_events",
                sql: "source IN ('health','app_error','job','provider')");
        }
    }
}
