using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSharedFinance : Migration
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

            migrationBuilder.AddColumn<Guid>(
                name: "group_category_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shared_at",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "saving_goals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "saving_goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "saving_goals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "budgets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_category_id",
                table: "budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "budgets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "group_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    icon_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_categories_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_share_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_share_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_share_rules_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_share_rules_financial_accounts_financial_account_id",
                        column: x => x.financial_account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_share_rules_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_group_share_rules_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_group_category_id",
                table: "transactions",
                column: "group_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_group_transacted_at",
                table: "transactions",
                columns: new[] { "group_id", "transacted_at" },
                filter: "group_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_group_category",
                table: "transactions",
                sql: "group_category_id IS NULL OR group_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_group_shareable",
                table: "transactions",
                sql: "group_id IS NULL OR transaction_type <> 'transfer'");

            migrationBuilder.CreateIndex(
                name: "IX_saving_goals_created_by_user_id",
                table: "saving_goals",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_saving_goals_group_id",
                table: "saving_goals",
                column: "group_id",
                filter: "group_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_saving_goals_owner",
                table: "saving_goals",
                sql: "(user_id IS NULL) <> (group_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked','transaction_shared','transaction_unshared','transaction_reclassified','budget_changed','goal_changed','goal_contributed','category_changed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly','group_budget','group_goal')");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_created_by_user_id",
                table: "budgets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_group_category_id",
                table: "budgets",
                column: "group_category_id");

            migrationBuilder.CreateIndex(
                name: "uq_budgets_group_category",
                table: "budgets",
                columns: new[] { "group_id", "category_id", "period_type" },
                unique: true,
                filter: "group_id IS NOT NULL AND category_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_budgets_group_group_category",
                table: "budgets",
                columns: new[] { "group_id", "group_category_id", "period_type" },
                unique: true,
                filter: "group_id IS NOT NULL AND group_category_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_budgets_group_total",
                table: "budgets",
                columns: new[] { "group_id", "period_type" },
                unique: true,
                filter: "group_id IS NOT NULL AND category_id IS NULL AND group_category_id IS NULL AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_budgets_group_category",
                table: "budgets",
                sql: "group_category_id IS NULL OR (group_id IS NOT NULL AND category_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_budgets_owner",
                table: "budgets",
                sql: "(user_id IS NULL) <> (group_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "uq_group_categories_group_slug",
                table: "group_categories",
                columns: new[] { "group_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_group_share_rules_category_id",
                table: "group_share_rules",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_share_rules_financial_account_id",
                table: "group_share_rules",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_share_rules_group_id",
                table: "group_share_rules",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_share_rules_user_active",
                table: "group_share_rules",
                columns: new[] { "user_id", "is_active" });

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_group_categories_group_category_id",
                table: "budgets",
                column: "group_category_id",
                principalTable: "group_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_groups_group_id",
                table: "budgets",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_users_created_by_user_id",
                table: "budgets",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_saving_goals_groups_group_id",
                table: "saving_goals",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_saving_goals_users_created_by_user_id",
                table: "saving_goals",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_group_categories_group_category_id",
                table: "transactions",
                column: "group_category_id",
                principalTable: "group_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_groups_group_id",
                table: "transactions",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budgets_group_categories_group_category_id",
                table: "budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_budgets_groups_group_id",
                table: "budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_budgets_users_created_by_user_id",
                table: "budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_saving_goals_groups_group_id",
                table: "saving_goals");

            migrationBuilder.DropForeignKey(
                name: "FK_saving_goals_users_created_by_user_id",
                table: "saving_goals");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_group_categories_group_category_id",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_groups_group_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "group_categories");

            migrationBuilder.DropTable(
                name: "group_share_rules");

            migrationBuilder.DropIndex(
                name: "IX_transactions_group_category_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_group_transacted_at",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_group_category",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_group_shareable",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_saving_goals_created_by_user_id",
                table: "saving_goals");

            migrationBuilder.DropIndex(
                name: "ix_saving_goals_group_id",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_saving_goals_owner",
                table: "saving_goals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities");

            migrationBuilder.DropCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts");

            migrationBuilder.DropIndex(
                name: "IX_budgets_created_by_user_id",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "IX_budgets_group_category_id",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "uq_budgets_group_category",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "uq_budgets_group_group_category",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "uq_budgets_group_total",
                table: "budgets");

            migrationBuilder.DropCheckConstraint(
                name: "chk_budgets_group_category",
                table: "budgets");

            migrationBuilder.DropCheckConstraint(
                name: "chk_budgets_owner",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "group_category_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "shared_at",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "saving_goals");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "group_category_id",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "budgets");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "saving_goals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "budgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_group_activities_type",
                table: "group_activities",
                sql: "type IN ('group_created','group_updated','member_joined','member_left','member_removed','member_updated','ownership_transferred','invitation_created','invitation_revoked')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_forecast_alerts_kind",
                table: "forecast_alerts",
                sql: "kind IN ('low_balance','budget_risk','bill_due','bill_status_check','income_late','price_change','anomaly')");
        }
    }
}
