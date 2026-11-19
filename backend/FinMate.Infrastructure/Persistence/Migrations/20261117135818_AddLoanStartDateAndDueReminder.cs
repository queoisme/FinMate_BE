using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanStartDateAndDueReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_notified_at",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "loans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Khoản đã có: coi ngày tạo là ngày bắt đầu vay.
            migrationBuilder.Sql("UPDATE loans SET start_date = (created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "loans",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "due_notified_at",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "loans");
        }
    }
}
