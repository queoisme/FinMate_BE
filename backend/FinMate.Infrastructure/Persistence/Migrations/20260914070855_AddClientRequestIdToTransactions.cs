using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRequestIdToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_transactions_user_client_request_id",
                table: "transactions",
                columns: new[] { "user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_transactions_user_client_request_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "transactions");
        }
    }
}
