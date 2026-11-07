using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeProfileOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "income_profile_override",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_income_profile_override",
                table: "users",
                sql: "income_profile_override IS NULL OR income_profile_override IN ('stable','variable')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_income_profile_override",
                table: "users");

            migrationBuilder.DropColumn(
                name: "income_profile_override",
                table: "users");
        }
    }
}
