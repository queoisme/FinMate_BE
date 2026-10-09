using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkReceiptScansToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transaction_id",
                table: "receipt_scans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_receipt_scans_transaction_id",
                table: "receipt_scans",
                column: "transaction_id",
                unique: true,
                filter: "transaction_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_receipt_scans_transactions_transaction_id",
                table: "receipt_scans",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_receipt_scans_transactions_transaction_id",
                table: "receipt_scans");

            migrationBuilder.DropIndex(
                name: "uq_receipt_scans_transaction_id",
                table: "receipt_scans");

            migrationBuilder.DropColumn(
                name: "transaction_id",
                table: "receipt_scans");
        }
    }
}
