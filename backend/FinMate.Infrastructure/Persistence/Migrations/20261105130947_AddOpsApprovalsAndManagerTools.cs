using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsApprovalsAndManagerTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_ticket_id",
                table: "support_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "canned_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canned_replies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ops_approval_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_approval_requests", x => x.id);
                    table.CheckConstraint("chk_ops_approval_requests_status", "status IN ('pending','approved','rejected','failed')");
                    table.CheckConstraint("chk_ops_approval_requests_two_people", "decided_by_user_id IS NULL OR requested_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id");
                    table.CheckConstraint("chk_ops_approval_requests_type", "type IN ('unlock_user','cancel_deletion','transfer_group_owner')");
                    table.ForeignKey(
                        name: "FK_ops_approval_requests_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ops_approval_requests_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ops_approval_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_parent",
                table: "support_tickets",
                column: "parent_ticket_id",
                filter: "parent_ticket_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ops_approval_requests_decided_by_user_id",
                table: "ops_approval_requests",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ops_approval_requests_requested_by_user_id",
                table: "ops_approval_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ops_approval_requests_ticket_id",
                table: "ops_approval_requests",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ops_approval_requests_status",
                table: "ops_approval_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_ops_approval_requests_one_pending",
                table: "ops_approval_requests",
                columns: new[] { "type", "target_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.AddForeignKey(
                name: "FK_support_tickets_support_tickets_parent_ticket_id",
                table: "support_tickets",
                column: "parent_ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_support_tickets_support_tickets_parent_ticket_id",
                table: "support_tickets");

            migrationBuilder.DropTable(
                name: "canned_replies");

            migrationBuilder.DropTable(
                name: "ops_approval_requests");

            migrationBuilder.DropIndex(
                name: "ix_support_tickets_parent",
                table: "support_tickets");

            migrationBuilder.DropColumn(
                name: "parent_ticket_id",
                table: "support_tickets");
        }
    }
}
