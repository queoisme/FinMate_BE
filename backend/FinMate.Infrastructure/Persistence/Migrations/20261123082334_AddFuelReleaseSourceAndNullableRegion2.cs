using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelReleaseSourceAndNullableRegion2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_market_sources_kind",
                table: "market_sources");

            migrationBuilder.DropCheckConstraint(
                name: "chk_fuel_prices_positive",
                table: "fuel_prices");

            migrationBuilder.AlterColumn<long>(
                name: "region2_price_cents",
                table: "fuel_prices",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string[]>(
                name: "source_domains",
                table: "fuel_prices",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_sources_kind",
                table: "market_sources",
                sql: "kind IN ('feed','price_table','page','fuel_release')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_fuel_prices_positive",
                table: "fuel_prices",
                sql: "region1_price_cents > 0 AND (region2_price_cents IS NULL OR region2_price_cents > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_market_sources_kind",
                table: "market_sources");

            migrationBuilder.DropCheckConstraint(
                name: "chk_fuel_prices_positive",
                table: "fuel_prices");

            migrationBuilder.DropColumn(
                name: "source_domains",
                table: "fuel_prices");

            // Bản cũ không có giá trần toàn quốc riêng: chép giá vùng 1 sang vùng 2 để giữ dòng, và đưa nguồn thông cáo về loại trang thường.
            migrationBuilder.Sql("UPDATE fuel_prices SET region2_price_cents = region1_price_cents WHERE region2_price_cents IS NULL;");
            migrationBuilder.Sql("UPDATE market_sources SET kind = 'page' WHERE kind = 'fuel_release';");

            migrationBuilder.AlterColumn<long>(
                name: "region2_price_cents",
                table: "fuel_prices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_sources_kind",
                table: "market_sources",
                sql: "kind IN ('feed','price_table','page')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_fuel_prices_positive",
                table: "fuel_prices",
                sql: "region1_price_cents > 0 AND region2_price_cents > 0");
        }
    }
}
