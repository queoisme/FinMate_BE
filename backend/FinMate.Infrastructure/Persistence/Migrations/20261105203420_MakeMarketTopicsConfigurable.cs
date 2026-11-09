using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeMarketTopicsConfigurable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_market_signals_topic",
                table: "market_signals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_market_events_topic",
                table: "market_events");

            migrationBuilder.AlterColumn<string>(
                name: "topic",
                table: "market_signals",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "topic",
                table: "market_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chủ đề mới không hợp lệ với CHECK cũ. Sự kiện còn phải đóng lại, vì nhiều sự kiện đang mở không được cùng mang chủ đề other.
            migrationBuilder.Sql("UPDATE market_signals SET topic = 'other' WHERE topic NOT IN ('fuel','electricity','water','food_price','other');");
            migrationBuilder.Sql("UPDATE market_events SET topic = 'other', status = 'expired' WHERE topic NOT IN ('fuel','electricity','water','food_price','other');");

            migrationBuilder.AlterColumn<string>(
                name: "topic",
                table: "market_signals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "topic",
                table: "market_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_signals_topic",
                table: "market_signals",
                sql: "topic IN ('fuel','electricity','water','food_price','other')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_events_topic",
                table: "market_events",
                sql: "topic IN ('fuel','electricity','water','food_price','other')");
        }
    }
}
