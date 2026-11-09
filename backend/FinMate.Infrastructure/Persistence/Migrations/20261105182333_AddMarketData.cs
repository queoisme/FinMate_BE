using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fuel_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    region1_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    region2_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_prices", x => x.id);
                    table.CheckConstraint("chk_fuel_prices_origin", "origin IN ('sources','manual')");
                    table.CheckConstraint("chk_fuel_prices_positive", "region1_price_cents > 0 AND region2_price_cents > 0");
                    table.ForeignKey(
                        name: "FK_fuel_prices_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "market_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ai_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_sources", x => x.id);
                    table.CheckConstraint("chk_market_sources_kind", "kind IN ('feed','price_table','page')");
                    table.ForeignKey(
                        name: "FK_market_sources_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "market_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    text_length = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_documents", x => x.id);
                    table.CheckConstraint("chk_market_documents_status", "status IN ('pending_ai','no_content','blocked','failed')");
                    table.ForeignKey(
                        name: "FK_market_documents_market_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "market_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_prices_created_by_user_id",
                table: "fuel_prices",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_prices_key_time",
                table: "fuel_prices",
                columns: new[] { "fuel_key", "first_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_documents_source",
                table: "market_documents",
                columns: new[] { "source_id", "fetched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_market_documents_status",
                table: "market_documents",
                columns: new[] { "status", "fetched_at" });

            migrationBuilder.CreateIndex(
                name: "uq_market_documents_url",
                table: "market_documents",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_market_sources_created_by_user_id",
                table: "market_sources",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_market_sources_url",
                table: "market_sources",
                column: "url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_prices");

            migrationBuilder.DropTable(
                name: "market_documents");

            migrationBuilder.DropTable(
                name: "market_sources");
        }
    }
}
