using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAlertThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alert_t1_pct",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "alert_t2_pct",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_users_alert_thresholds",
                table: "users",
                sql: "(alert_t1_pct IS NULL AND alert_t2_pct IS NULL) OR (alert_t1_pct IS NOT NULL AND alert_t2_pct IS NOT NULL AND alert_t1_pct >= 1 AND alert_t1_pct < alert_t2_pct AND alert_t2_pct <= 99)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_users_alert_thresholds",
                table: "users");

            migrationBuilder.DropColumn(
                name: "alert_t1_pct",
                table: "users");

            migrationBuilder.DropColumn(
                name: "alert_t2_pct",
                table: "users");
        }
    }
}
