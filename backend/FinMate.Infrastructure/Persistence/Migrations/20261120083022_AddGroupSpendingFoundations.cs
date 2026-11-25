using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSpendingFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_type",
                table: "groups");

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                table: "groups",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "closing_day",
                table: "groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "groups",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "debt_settlement_method",
                table: "groups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ai_optimized");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "groups",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon",
                table: "groups",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_new_transaction",
                table: "groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "require_approval_above_cents",
                table: "groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_model",
                table: "groups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "shared_fund");

            migrationBuilder.AddColumn<bool>(
                name: "weekly_summary",
                table: "groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "weekly_summary_day",
                table: "groups",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "weekly_summary_time",
                table: "groups",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<Guid>(
                name: "default_financial_account_id",
                table: "group_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "declined_at",
                table: "group_invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message",
                table: "group_invitations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_approval_threshold",
                table: "groups",
                sql: "require_approval_above_cents IS NULL OR require_approval_above_cents > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_closing_day",
                table: "groups",
                sql: "closing_day IS NULL OR closing_day BETWEEN 1 AND 28");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_debt_settlement_method",
                table: "groups",
                sql: "debt_settlement_method IN ('ai_optimized','manual')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_settlement_model",
                table: "groups",
                sql: "settlement_model IN ('shared_fund','split_bill')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_type",
                table: "groups",
                sql: "type IN ('family','other','friends','travel','colleagues','roommates')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_weekly_summary_day",
                table: "groups",
                sql: "weekly_summary_day BETWEEN 1 AND 7");

            migrationBuilder.CreateIndex(
                name: "IX_group_members_default_financial_account_id",
                table: "group_members",
                column: "default_financial_account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_group_members_financial_accounts_default_financial_account_~",
                table: "group_members",
                column: "default_financial_account_id",
                principalTable: "financial_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_group_members_financial_accounts_default_financial_account_~",
                table: "group_members");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_approval_threshold",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_closing_day",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_debt_settlement_method",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_settlement_model",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_type",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "chk_groups_weekly_summary_day",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_group_members_default_financial_account_id",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "closing_day",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "debt_settlement_method",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "description",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "notify_on_new_transaction",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "require_approval_above_cents",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "settlement_model",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "weekly_summary",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "weekly_summary_day",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "weekly_summary_time",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "default_financial_account_id",
                table: "group_members");

            migrationBuilder.DropColumn(
                name: "declined_at",
                table: "group_invitations");

            migrationBuilder.DropColumn(
                name: "message",
                table: "group_invitations");

            migrationBuilder.AddCheckConstraint(
                name: "chk_groups_type",
                table: "groups",
                sql: "type IN ('family','other')");
        }
    }
}
