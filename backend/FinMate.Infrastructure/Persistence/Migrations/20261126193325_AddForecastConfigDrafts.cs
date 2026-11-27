using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 95 (A): bản nháp tham số dự báo trên <c>system_config_versions</c>. Dòng cũ không đổi nghĩa: phạm vi, phiên bản gốc, người duyệt đều null.
    /// Trạng thái lưu dạng snake_case (<c>pending_approval</c>); <c>active</c>/<c>archived</c> giữ nguyên chữ.
    /// </summary>
    public partial class AddForecastConfigDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_status",
                table: "system_config_versions");

            migrationBuilder.AlterColumn<int>(
                name: "version",
                table: "system_config_versions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "system_config_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<int>(
                name: "base_version",
                table: "system_config_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "conflicts",
                table: "system_config_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "system_config_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "system_config_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "system_config_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "system_config_versions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "system_config_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "system_config_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_config_versions_drafts",
                table: "system_config_versions",
                columns: new[] { "name", "scope", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_system_config_versions_pending",
                table: "system_config_versions",
                columns: new[] { "name", "scope" },
                unique: true,
                filter: "status = 'pending_approval'");

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_approved_by",
                table: "system_config_versions",
                sql: "scope IS NULL OR status NOT IN ('active','archived') OR decided_by IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_draft_scope",
                table: "system_config_versions",
                sql: "status IN ('active','archived') OR scope IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_numbered",
                table: "system_config_versions",
                sql: "(status IN ('active','archived')) = (version IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_status",
                table: "system_config_versions",
                sql: "status IN ('active','archived','draft','pending_approval','rejected','cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_two_people",
                table: "system_config_versions",
                sql: "decided_by IS NULL OR decided_by <> created_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bản nháp chưa có số phiên bản: phải bỏ trước khi cột version trở lại NOT NULL.
            migrationBuilder.Sql("DELETE FROM system_config_versions WHERE version IS NULL;");

            migrationBuilder.DropIndex(
                name: "ix_system_config_versions_drafts",
                table: "system_config_versions");

            migrationBuilder.DropIndex(
                name: "uq_system_config_versions_pending",
                table: "system_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_approved_by",
                table: "system_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_draft_scope",
                table: "system_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_numbered",
                table: "system_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_status",
                table: "system_config_versions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_system_config_versions_two_people",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "base_version",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "conflicts",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "system_config_versions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "system_config_versions");

            migrationBuilder.AlterColumn<int>(
                name: "version",
                table: "system_config_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "system_config_versions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "chk_system_config_versions_status",
                table: "system_config_versions",
                sql: "status IN ('active','archived')");
        }
    }
}
