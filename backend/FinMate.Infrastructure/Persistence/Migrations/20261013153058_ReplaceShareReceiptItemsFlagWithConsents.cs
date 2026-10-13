using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceShareReceiptItemsFlagWithConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_receipt_items_with_ai",
                table: "users");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_consent_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ai_consent_version",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terms_accepted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "terms_version",
                table: "users",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_consent_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ai_consent_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "terms_accepted_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "terms_version",
                table: "users");

            migrationBuilder.AddColumn<bool>(
                name: "share_receipt_items_with_ai",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
