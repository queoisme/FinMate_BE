using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_notification_logs_status",
                table: "notification_logs");

            migrationBuilder.AddColumn<Guid>(
                name: "duplicate_of_transaction_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "duplicate_status",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_transaction_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "merged_sources",
                table: "transactions",
                type: "text[]",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_duplicate_of_transaction_id",
                table: "transactions",
                column: "duplicate_of_transaction_id",
                filter: "duplicate_of_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_merged_into_transaction_id",
                table: "transactions",
                column: "merged_into_transaction_id",
                filter: "merged_into_transaction_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_duplicate_link",
                table: "transactions",
                sql: "(duplicate_status IS NULL) = (duplicate_of_transaction_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_duplicate_not_self",
                table: "transactions",
                sql: "duplicate_of_transaction_id IS NULL OR duplicate_of_transaction_id <> id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_duplicate_status",
                table: "transactions",
                sql: "duplicate_status IS NULL OR duplicate_status IN ('suspected','dismissed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_merged_deleted",
                table: "transactions",
                sql: "merged_into_transaction_id IS NULL OR deleted_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_merged_not_self",
                table: "transactions",
                sql: "merged_into_transaction_id IS NULL OR merged_into_transaction_id <> id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_merged_sources",
                table: "transactions",
                sql: "merged_sources IS NULL OR merged_sources <@ ARRAY['notification','manual','voice','receipt','support']::text[]");

            migrationBuilder.AddCheckConstraint(
                name: "chk_notification_logs_status",
                table: "notification_logs",
                sql: "status IN ('pending','processed','failed','ignored','duplicate')");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_transactions_duplicate_of_transaction_id",
                table: "transactions",
                column: "duplicate_of_transaction_id",
                principalTable: "transactions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_transactions_merged_into_transaction_id",
                table: "transactions",
                column: "merged_into_transaction_id",
                principalTable: "transactions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_transactions_duplicate_of_transaction_id",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_transactions_merged_into_transaction_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_duplicate_of_transaction_id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_merged_into_transaction_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_duplicate_link",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_duplicate_not_self",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_duplicate_status",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_merged_deleted",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_merged_not_self",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_merged_sources",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_notification_logs_status",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "duplicate_of_transaction_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "duplicate_status",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "merged_into_transaction_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "merged_sources",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_notification_logs_status",
                table: "notification_logs",
                sql: "status IN ('pending','processed','failed','ignored')");
        }
    }
}
