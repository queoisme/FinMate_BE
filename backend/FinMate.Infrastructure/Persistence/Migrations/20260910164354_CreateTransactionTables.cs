using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateTransactionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notification_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notification_body = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.id);
                    table.CheckConstraint("chk_notification_logs_status", "status IN ('pending','processed','failed','ignored')");
                    table.ForeignKey(
                        name: "FK_notification_logs_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_notification_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saving_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_cents = table.Column<long>(type: "bigint", nullable: false),
                    saved_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saving_goals", x => x.id);
                    table.ForeignKey(
                        name: "FK_saving_goals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    notification_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    classifier_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    classifier_confidence = table.Column<double>(type: "double precision", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: true),
                    transaction_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    merchant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    transacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    balance_after_cents = table.Column<long>(type: "bigint", nullable: true),
                    extraction_confidence = table.Column<double>(type: "double precision", nullable: true),
                    category_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    categorization_confidence = table.Column<double>(type: "double precision", nullable: true),
                    is_potential_duplicate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    duplicate_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    classifier_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    extractor_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    categorizer_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    processing_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_results", x => x.id);
                    table.CheckConstraint("chk_ai_results_pipeline_result", "pipeline_result IN ('financial','non_financial','uncertain','extraction_failed','error')");
                    table.ForeignKey(
                        name: "FK_ai_results_notification_logs_notification_log_id",
                        column: x => x.notification_log_id,
                        principalTable: "notification_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    saving_goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    merchant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    transacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    balance_after_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                    table.CheckConstraint("chk_transactions_source", "source IN ('notification','manual')");
                    table.CheckConstraint("chk_transactions_status", "status IN ('draft','confirmed')");
                    table.CheckConstraint("chk_transactions_transaction_type", "transaction_type IN ('debit','credit')");
                    table.ForeignKey(
                        name: "FK_transactions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transactions_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transactions_notification_logs_notification_log_id",
                        column: x => x.notification_log_id,
                        principalTable: "notification_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transactions_saving_goals_saving_goal_id",
                        column: x => x.saving_goal_id,
                        principalTable: "saving_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_ai_results_notification_log_id",
                table: "ai_results",
                column: "notification_log_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_financial_account_id",
                table: "notification_logs",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "idx_notification_logs_status_retry_count",
                table: "notification_logs",
                columns: new[] { "status", "retry_count" });

            migrationBuilder.CreateIndex(
                name: "idx_notification_logs_user_content_hash",
                table: "notification_logs",
                columns: new[] { "user_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "idx_saving_goals_user_id",
                table: "saving_goals",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_notification_log_id",
                table: "transactions",
                column: "notification_log_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_saving_goal_id",
                table: "transactions",
                column: "saving_goal_id");

            migrationBuilder.CreateIndex(
                name: "idx_transactions_category_id",
                table: "transactions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "idx_transactions_financial_account_id",
                table: "transactions",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "idx_transactions_user_transacted_at",
                table: "transactions",
                columns: new[] { "user_id", "transacted_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_results");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropTable(
                name: "saving_goals");
        }
    }
}
