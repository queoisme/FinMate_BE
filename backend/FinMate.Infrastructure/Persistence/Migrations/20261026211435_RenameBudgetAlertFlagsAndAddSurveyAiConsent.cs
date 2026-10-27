using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameBudgetAlertFlagsAndAddSurveyAiConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "alert_90_sent_at",
                table: "budget_periods",
                newName: "alert_t2_sent_at");

            migrationBuilder.RenameColumn(
                name: "alert_70_sent_at",
                table: "budget_periods",
                newName: "alert_t1_sent_at");

            migrationBuilder.RenameColumn(
                name: "alert_100_sent_at",
                table: "budget_periods",
                newName: "alert_over_sent_at");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "survey_ai_consent_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "survey_ai_consent_version",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_survey_ai_consent",
                table: "users",
                sql: "(survey_ai_consent_version IS NULL) = (survey_ai_consent_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_survey_ai_consent",
                table: "users");

            migrationBuilder.DropColumn(
                name: "survey_ai_consent_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "survey_ai_consent_version",
                table: "users");

            migrationBuilder.RenameColumn(
                name: "alert_t2_sent_at",
                table: "budget_periods",
                newName: "alert_90_sent_at");

            migrationBuilder.RenameColumn(
                name: "alert_t1_sent_at",
                table: "budget_periods",
                newName: "alert_70_sent_at");

            migrationBuilder.RenameColumn(
                name: "alert_over_sent_at",
                table: "budget_periods",
                newName: "alert_100_sent_at");
        }
    }
}
