using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingGoalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals");

            migrationBuilder.AddColumn<string>(
                name: "contribution_frequency",
                table: "saving_goals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon",
                table: "saving_goals",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "initial_saved_cents",
                table: "saving_goals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_contribution_frequency",
                table: "saving_goals",
                sql: "contribution_frequency IS NULL OR contribution_frequency IN ('weekly','monthly','custom')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_initial_saved_cents",
                table: "saving_goals",
                sql: "initial_saved_cents >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals",
                sql: "status IN ('draft','active','completed','cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_contribution_frequency",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_initial_saved_cents",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "contribution_frequency",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "initial_saved_cents",
                table: "saving_goals");

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_status",
                table: "saving_goals",
                sql: "status IN ('active','completed','cancelled')");
        }
    }
}
