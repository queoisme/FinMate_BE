using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemConfigVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_config_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_config_versions", x => x.id);
                    table.CheckConstraint("chk_system_config_versions_content_object", "jsonb_typeof(content) = 'object'");
                    table.CheckConstraint("chk_system_config_versions_status", "status IN ('active','archived')");
                });

            migrationBuilder.CreateIndex(
                name: "uq_system_config_versions_active",
                table: "system_config_versions",
                column: "name",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "uq_system_config_versions_name_version",
                table: "system_config_versions",
                columns: new[] { "name", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_config_versions");
        }
    }
}
