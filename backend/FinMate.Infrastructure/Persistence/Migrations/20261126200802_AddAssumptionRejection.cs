using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>Phase 95: giả định kinh tế có trạng thái <c>rejected</c> kèm ghi chú, và DB chặn tự duyệt / tự từ chối (miễn trừ job CPI).</summary>
    public partial class AddAssumptionRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_economic_assumptions_status",
                table: "economic_assumptions");

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "economic_assumptions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rejected_at",
                table: "economic_assumptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rejected_by",
                table: "economic_assumptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_economic_assumptions_status",
                table: "economic_assumptions",
                sql: "status IN ('draft','approved','retired','rejected')");

            // NOT VALID: dòng đã có (trước Phase 95 có thể tự duyệt) không bị kiểm lại; mọi dòng ghi từ giờ bị kiểm.
            migrationBuilder.Sql(
                "ALTER TABLE economic_assumptions ADD CONSTRAINT chk_economic_assumptions_two_people CHECK "
                + "((approved_by IS NULL OR approved_by <> created_by OR created_by = '00000000-0000-0000-0000-00000000a1c5') AND (rejected_by IS NULL OR rejected_by <> created_by)) NOT VALID;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_economic_assumptions_status",
                table: "economic_assumptions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_economic_assumptions_two_people",
                table: "economic_assumptions");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "economic_assumptions");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "economic_assumptions");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                table: "economic_assumptions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_economic_assumptions_status",
                table: "economic_assumptions",
                sql: "status IN ('draft','approved','retired')");
        }
    }
}
