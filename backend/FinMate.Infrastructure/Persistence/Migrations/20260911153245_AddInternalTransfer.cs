using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_transaction_type",
                table: "transactions");

            migrationBuilder.AddColumn<Guid>(
                name: "counter_account_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_transactions_counter_account_id",
                table: "transactions",
                column: "counter_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_transaction_type",
                table: "transactions",
                sql: "transaction_type IN ('debit','credit','transfer')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions",
                sql: "(transaction_type = 'transfer' AND counter_account_id IS NOT NULL AND counter_account_id <> financial_account_id AND category_id IS NULL) OR (transaction_type <> 'transfer' AND counter_account_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_financial_accounts_counter_account_id",
                table: "transactions",
                column: "counter_account_id",
                principalTable: "financial_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_financial_accounts_counter_account_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "idx_transactions_counter_account_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_transaction_type",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_transfer_shape",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "counter_account_id",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_transaction_type",
                table: "transactions",
                sql: "transaction_type IN ('debit','credit')");
        }
    }
}
