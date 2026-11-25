using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupBillsAndDebts : Migration
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
                name: "group_bills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    paid_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    split_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_bills", x => x.id);
                    table.CheckConstraint("chk_group_bills_amount", "amount_cents > 0");
                    table.CheckConstraint("chk_group_bills_split_mode", "split_mode IN ('equal','custom')");
                    table.ForeignKey(
                        name: "FK_group_bills_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_bills_group_categories_group_category_id",
                        column: x => x.group_category_id,
                        principalTable: "group_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_bills_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_bills_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_bills_users_paid_by_user_id",
                        column: x => x.paid_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_debt_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debtor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_debt_reminders", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_debt_reminders_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_debt_reminders_users_debtor_user_id",
                        column: x => x.debtor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_debt_reminders_users_sent_by_user_id",
                        column: x => x.sent_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_settlements", x => x.id);
                    table.CheckConstraint("chk_group_settlements_amount", "amount_cents > 0");
                    table.ForeignKey(
                        name: "FK_group_settlements_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_settlements_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_settlements_users_from_user_id",
                        column: x => x.from_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_group_settlements_users_to_user_id",
                        column: x => x.to_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_bill_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    share_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_bill_splits", x => x.id);
                    table.CheckConstraint("chk_group_bill_splits_share", "share_cents > 0");
                    table.ForeignKey(
                        name: "FK_group_bill_splits_group_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "group_bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_bill_splits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed','bill_created','settlement_recorded','debt_reminded','fund_created','fund_updated','fund_archived','fund_contributed','fund_withdrawn','fund_entry_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest','group_debt_reminder')");

            migrationBuilder.CreateIndex(
                name: "ix_group_bill_splits_bill",
                table: "group_bill_splits",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_bill_splits_user",
                table: "group_bill_splits",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_bills_category_id",
                table: "group_bills",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_bills_created_by_user_id",
                table: "group_bills",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_bills_group_category_id",
                table: "group_bills",
                column: "group_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_bills_paid_by_user_id",
                table: "group_bills",
                column: "paid_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_bills_group_occurred",
                table: "group_bills",
                columns: new[] { "group_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_group_debt_reminders_debtor_user_id",
                table: "group_debt_reminders",
                column: "debtor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_debt_reminders_sent_by_user_id",
                table: "group_debt_reminders",
                column: "sent_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_debt_reminders_debtor",
                table: "group_debt_reminders",
                columns: new[] { "group_id", "debtor_user_id", "sent_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_group_settlements_created_by_user_id",
                table: "group_settlements",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_settlements_from_user_id",
                table: "group_settlements",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_settlements_to_user_id",
                table: "group_settlements",
                column: "to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_settlements_group_created",
                table: "group_settlements",
                columns: new[] { "group_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_bill_splits");

            migrationBuilder.DropTable(
                name: "group_debt_reminders");

            migrationBuilder.DropTable(
                name: "group_settlements");

            migrationBuilder.DropTable(
                name: "group_bills");

            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed','fund_created','fund_updated','fund_archived','fund_contributed','fund_withdrawn','fund_entry_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal','market_change','listener_health','group_fund_negative','group_fund_removed','purchase_reminder','weekly_digest')");
        }
    }
}
