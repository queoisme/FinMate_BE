using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtRepaymentHint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results");

            migrationBuilder.AddColumn<string>(
                name: "debt_hint_strength",
                table: "ai_results",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_debt_hint_strength",
                table: "ai_results",
                sql: "debt_hint_strength IS NULL OR (relation_hint = 'debt_repayment' AND debt_hint_strength IN ('strong','weak'))");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results",
                sql: "relation_hint IS NULL OR relation_hint IN ('refund','reversal','transfer','debt_repayment')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_debt_hint_strength",
                table: "ai_results");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "debt_hint_strength",
                table: "ai_results");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results",
                sql: "relation_hint IS NULL OR relation_hint IN ('refund','reversal','transfer')");
        }
    }
}
