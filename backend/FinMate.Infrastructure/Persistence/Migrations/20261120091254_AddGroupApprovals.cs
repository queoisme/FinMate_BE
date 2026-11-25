using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.CreateTable(
                name: "group_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_approvals", x => x.id);
                    table.CheckConstraint("chk_group_approvals_decision", "(status = 'pending') = (decided_at IS NULL)");
                    table.CheckConstraint("chk_group_approvals_kind", "kind IN ('bill','fund_entry')");
                    table.CheckConstraint("chk_group_approvals_status", "status IN ('pending','approved','rejected')");
                    table.ForeignKey(
                        name: "FK_group_approvals_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_approvals_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_approvals_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed','bill_created','approval_requested','approval_approved','approval_rejected','settlement_recorded','debt_reminded','fund_created','fund_updated','fund_archived','fund_contributed','fund_withdrawn','fund_entry_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder','group_approval_req','group_approval_res')");

            migrationBuilder.CreateIndex(
                name: "IX_group_approvals_decided_by_user_id",
                table: "group_approvals",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_approvals_requested_by_user_id",
                table: "group_approvals",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_approvals_group_status",
                table: "group_approvals",
                columns: new[] { "group_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "uq_group_approvals_client_request",
                table: "group_approvals",
                columns: new[] { "group_id", "requested_by_user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_approvals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed','bill_created','settlement_recorded','debt_reminded','fund_created','fund_updated','fund_archived','fund_contributed','fund_withdrawn','fund_entry_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder')");
        }
    }
}
