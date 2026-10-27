using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceMethodsAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onboarding_completed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "onboarding_status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<Guid>(
                name: "goal_template_id",
                table: "saving_goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_method_id",
                table: "saving_goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "finance_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    goal_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    param_defs = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    eligibility = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    why_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    starter_steps = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_methods", x => x.id);
                    table.CheckConstraint("chk_finance_methods_eligibility", "jsonb_typeof(eligibility) = 'object'");
                    table.CheckConstraint("chk_finance_methods_goal_mode", "goal_mode IN ('reserve_first','envelope','available_to_spend','track_only')");
                    table.CheckConstraint("chk_finance_methods_param_defs", "jsonb_typeof(param_defs) = 'array'");
                    table.CheckConstraint("chk_finance_methods_role", "role IN ('primary','supporting','both')");
                    table.CheckConstraint("chk_finance_methods_starter_steps", "jsonb_typeof(starter_steps) = 'array'");
                    table.CheckConstraint("chk_finance_methods_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "goal_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_params = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_templates", x => x.id);
                    table.CheckConstraint("chk_goal_templates_default_params", "jsonb_typeof(default_params) = 'object'");
                });

            migrationBuilder.CreateTable(
                name: "survey_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    group_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    show_if = table.Column<string>(type: "jsonb", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_questions", x => x.id);
                    table.CheckConstraint("chk_survey_questions_show_if", "show_if IS NULL OR jsonb_typeof(show_if) = 'object'");
                });

            migrationBuilder.CreateTable(
                name: "user_survey_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    option_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_survey_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_survey_answers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "method_recommendations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reasons = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    signals_snapshot = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    method_versions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "shown"),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_method_recommendations", x => x.id);
                    table.CheckConstraint("chk_method_recommendations_decided", "(outcome = 'shown') = (decided_at IS NULL)");
                    table.CheckConstraint("chk_method_recommendations_method_versions", "jsonb_typeof(method_versions) = 'object'");
                    table.CheckConstraint("chk_method_recommendations_outcome", "outcome IN ('shown','accepted','changed','skipped')");
                    table.CheckConstraint("chk_method_recommendations_prompt_version", "source <> 'ai' OR prompt_version IS NOT NULL");
                    table.CheckConstraint("chk_method_recommendations_reasons", "jsonb_typeof(reasons) = 'array'");
                    table.CheckConstraint("chk_method_recommendations_signals_snapshot", "jsonb_typeof(signals_snapshot) = 'object'");
                    table.CheckConstraint("chk_method_recommendations_source", "source IN ('ai','rule_fallback')");
                    table.CheckConstraint("chk_method_recommendations_support_differs", "support_method_id IS NULL OR support_method_id <> primary_method_id");
                    table.ForeignKey(
                        name: "FK_method_recommendations_finance_methods_primary_method_id",
                        column: x => x.primary_method_id,
                        principalTable: "finance_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_method_recommendations_finance_methods_support_method_id",
                        column: x => x.support_method_id,
                        principalTable: "finance_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_method_recommendations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "method_goal_templates",
                columns: table => new
                {
                    method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_method_goal_templates", x => new { x.method_id, x.template_id });
                    table.CheckConstraint("chk_method_goal_templates_conditions", "conditions IS NULL OR jsonb_typeof(conditions) = 'object'");
                    table.ForeignKey(
                        name: "FK_method_goal_templates_finance_methods_method_id",
                        column: x => x.method_id,
                        principalTable: "finance_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_method_goal_templates_goal_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "goal_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    signals = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_options", x => x.id);
                    table.CheckConstraint("chk_survey_options_signals", "jsonb_typeof(signals) = 'object'");
                    table.ForeignKey(
                        name: "FK_survey_options_survey_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "survey_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_version = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    chosen_via = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_methods", x => x.id);
                    table.CheckConstraint("chk_user_methods_chosen_via", "chosen_via IN ('recommended','manual')");
                    table.CheckConstraint("chk_user_methods_ended", "(status = 'ended') = (ended_at IS NOT NULL)");
                    table.CheckConstraint("chk_user_methods_method_version", "method_version >= 1");
                    table.CheckConstraint("chk_user_methods_params", "jsonb_typeof(params) = 'object'");
                    table.CheckConstraint("chk_user_methods_period", "ended_at IS NULL OR ended_at >= started_at");
                    table.CheckConstraint("chk_user_methods_role", "role IN ('primary','supporting')");
                    table.CheckConstraint("chk_user_methods_status", "status IN ('active','ended')");
                    table.ForeignKey(
                        name: "FK_user_methods_finance_methods_method_id",
                        column: x => x.method_id,
                        principalTable: "finance_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_methods_method_recommendations_recommendation_id",
                        column: x => x.recommendation_id,
                        principalTable: "method_recommendations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_methods_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_onboarding_completed_at",
                table: "users",
                sql: "(onboarding_status = 'pending') = (onboarding_completed_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_onboarding_status",
                table: "users",
                sql: "onboarding_status IN ('pending','completed','skipped')");

            // Coi mọi tài khoản ĐÃ CÓ là đã xong khảo sát. Không backfill thì cột mới mặc định 'pending'
            // và toàn bộ người dùng hiện tại bị đẩy vào khảo sát — họ dùng app từ trước khi nó tồn tại.
            // Hai cột đổi CÙNG LÚC để không vi phạm chk_users_onboarding_completed_at.
            migrationBuilder.Sql(
                "UPDATE users SET onboarding_status = 'completed', onboarding_completed_at = NOW() "
                + "WHERE onboarding_status = 'pending';");

            migrationBuilder.CreateIndex(
                name: "IX_saving_goals_goal_template_id",
                table: "saving_goals",
                column: "goal_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_saving_goals_user_method_id",
                table: "saving_goals",
                column: "user_method_id");

            migrationBuilder.CreateIndex(
                name: "idx_finance_methods_active_order",
                table: "finance_methods",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "uq_finance_methods_code",
                table: "finance_methods",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_goal_templates_code",
                table: "goal_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_method_goal_templates_template_id",
                table: "method_goal_templates",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_method_recommendations_primary_method_id",
                table: "method_recommendations",
                column: "primary_method_id");

            migrationBuilder.CreateIndex(
                name: "IX_method_recommendations_support_method_id",
                table: "method_recommendations",
                column: "support_method_id");

            migrationBuilder.CreateIndex(
                name: "idx_method_recommendations_user_created",
                table: "method_recommendations",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_survey_options_question_code",
                table: "survey_options",
                columns: new[] { "question_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_survey_questions_active_order",
                table: "survey_questions",
                columns: new[] { "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "uq_survey_questions_code",
                table: "survey_questions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_methods_method_id",
                table: "user_methods",
                column: "method_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_methods_recommendation_id",
                table: "user_methods",
                column: "recommendation_id");

            migrationBuilder.CreateIndex(
                name: "idx_user_methods_user_status",
                table: "user_methods",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_user_methods_active_role",
                table: "user_methods",
                columns: new[] { "user_id", "role" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "uq_user_survey_answers_user_question",
                table: "user_survey_answers",
                columns: new[] { "user_id", "question_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_saving_goals_goal_templates_goal_template_id",
                table: "saving_goals",
                column: "goal_template_id",
                principalTable: "goal_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_saving_goals_user_methods_user_method_id",
                table: "saving_goals",
                column: "user_method_id",
                principalTable: "user_methods",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_saving_goals_goal_templates_goal_template_id",
                table: "saving_goals");

            migrationBuilder.DropForeignKey(
                name: "FK_saving_goals_user_methods_user_method_id",
                table: "saving_goals");

            migrationBuilder.DropTable(
                name: "method_goal_templates");

            migrationBuilder.DropTable(
                name: "survey_options");

            migrationBuilder.DropTable(
                name: "user_methods");

            migrationBuilder.DropTable(
                name: "user_survey_answers");

            migrationBuilder.DropTable(
                name: "goal_templates");

            migrationBuilder.DropTable(
                name: "survey_questions");

            migrationBuilder.DropTable(
                name: "method_recommendations");

            migrationBuilder.DropTable(
                name: "finance_methods");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_onboarding_completed_at",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "chk_users_onboarding_status",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_saving_goals_goal_template_id",
                table: "saving_goals");

            migrationBuilder.DropIndex(
                name: "IX_saving_goals_user_method_id",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "onboarding_completed_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "onboarding_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "goal_template_id",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "user_method_id",
                table: "saving_goals");
        }
    }
}
