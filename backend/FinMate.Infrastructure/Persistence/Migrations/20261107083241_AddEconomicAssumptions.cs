using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEconomicAssumptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "economic_assumptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    rate_bps = table.Column<int>(type: "integer", nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_name = table.Column<string>(type: "text", nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_economic_assumptions", x => x.id);
                    table.CheckConstraint("chk_economic_assumptions_rate", "rate_bps BETWEEN -10000 AND 100000");
                    table.CheckConstraint("chk_economic_assumptions_source", "status = 'draft' OR (length(btrim(source_name)) > 0 AND length(btrim(source_url)) > 0)");
                    table.CheckConstraint("chk_economic_assumptions_status", "status IN ('draft','approved','retired')");
                    table.CheckConstraint("chk_economic_assumptions_version", "version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "uq_economic_assumptions_key_version",
                table: "economic_assumptions",
                columns: new[] { "key", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "economic_assumptions");
        }
    }
}
