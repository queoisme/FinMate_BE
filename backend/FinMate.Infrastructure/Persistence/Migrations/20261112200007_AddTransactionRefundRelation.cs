using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionRefundRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies");

            migrationBuilder.AddColumn<bool>(
                name: "is_reversed",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "related_transaction_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "relation",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "total_refunded_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "relation_hint",
                table: "ai_results",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_related_transaction_id",
                table: "transactions",
                column: "related_transaction_id",
                filter: "related_transaction_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_related_has_relation",
                table: "transactions",
                sql: "related_transaction_id IS NULL OR relation IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_related_not_self",
                table: "transactions",
                sql: "related_transaction_id IS NULL OR related_transaction_id <> id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_relation",
                table: "transactions",
                sql: "relation IS NULL OR relation IN ('refund','reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_relation_money_in",
                table: "transactions",
                sql: "relation IS NULL OR transaction_type = 'credit'");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_reversal_has_original",
                table: "transactions",
                sql: "relation IS NULL OR relation <> 'reversal' OR related_transaction_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_reversed_debit",
                table: "transactions",
                sql: "is_reversed = false OR transaction_type = 'debit'");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies",
                sql: "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious','refunded','reversed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results",
                sql: "relation_hint IS NULL OR relation_hint IN ('refund','reversal')");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_transactions_related_transaction_id",
                table: "transactions",
                column: "related_transaction_id",
                principalTable: "transactions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_transactions_related_transaction_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_related_transaction_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_related_has_relation",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_related_not_self",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_relation",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_relation_money_in",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_reversal_has_original",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_reversed_debit",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "is_reversed",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "related_transaction_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "relation",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "total_refunded_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "relation_hint",
                table: "ai_results");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies",
                sql: "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious')");
        }
    }
}
