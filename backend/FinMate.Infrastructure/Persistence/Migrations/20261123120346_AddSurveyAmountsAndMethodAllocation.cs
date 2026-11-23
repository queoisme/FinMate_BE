using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyAmountsAndMethodAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "amount_vnd",
                table: "user_survey_answers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "answer_type",
                table: "survey_questions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "choice");

            migrationBuilder.AddColumn<string>(
                name: "category_slug",
                table: "survey_questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_role",
                table: "survey_questions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "share_max_pct",
                table: "survey_options",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "share_min_pct",
                table: "survey_options",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allocation",
                table: "finance_methods",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: "chk_user_survey_answers_amount",
                table: "user_survey_answers",
                sql: "amount_vnd IS NULL OR amount_vnd >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_finance_methods_allocation",
                table: "finance_methods",
                sql: "jsonb_typeof(allocation) = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_user_survey_answers_amount",
                table: "user_survey_answers");

            migrationBuilder.DropCheckConstraint(
                name: "chk_finance_methods_allocation",
                table: "finance_methods");

            migrationBuilder.DropColumn(
                name: "amount_vnd",
                table: "user_survey_answers");

            migrationBuilder.DropColumn(
                name: "answer_type",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "category_slug",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "plan_role",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "share_max_pct",
                table: "survey_options");

            migrationBuilder.DropColumn(
                name: "share_min_pct",
                table: "survey_options");

            migrationBuilder.DropColumn(
                name: "allocation",
                table: "finance_methods");
        }
    }
}
