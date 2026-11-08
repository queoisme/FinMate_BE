using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ops_error_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exception_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source_context = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    message_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    request_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_error_events", x => x.id);
                    table.CheckConstraint("chk_ops_error_events_source", "source IN ('health','app_error','job','provider')");
                });

            migrationBuilder.CreateTable(
                name: "ops_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    technical_detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notification_count = table.Column<int>(type: "integer", nullable: false),
                    resolved_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_incidents", x => x.id);
                    table.CheckConstraint("chk_ops_incidents_severity", "severity IN ('normal','high','critical')");
                    table.CheckConstraint("chk_ops_incidents_source", "source IN ('health','app_error','job','provider')");
                    table.CheckConstraint("chk_ops_incidents_status", "status IN ('open','acknowledged','resolved')");
                    table.ForeignKey(
                        name: "FK_ops_incidents_users_acknowledged_by_user_id",
                        column: x => x.acknowledged_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ops_incidents_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ops_service_status",
                columns: table => new
                {
                    service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    consecutive_successes = table.Column<int>(type: "integer", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_healthy_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_service_status", x => x.service);
                    table.CheckConstraint("chk_ops_service_status_state", "state IN ('healthy','unhealthy','disabled','unknown')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ops_error_events_created_at",
                table: "ops_error_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ops_error_events_fingerprint",
                table: "ops_error_events",
                columns: new[] { "fingerprint", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_incidents_acknowledged_by_user_id",
                table: "ops_incidents",
                column: "acknowledged_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ops_incidents_resolved_by_user_id",
                table: "ops_incidents",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ops_incidents_status",
                table: "ops_incidents",
                columns: new[] { "status", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "uq_ops_incidents_one_active",
                table: "ops_incidents",
                column: "fingerprint",
                unique: true,
                filter: "status <> 'resolved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_error_events");

            migrationBuilder.DropTable(
                name: "ops_incidents");

            migrationBuilder.DropTable(
                name: "ops_service_status");
        }
    }
}
