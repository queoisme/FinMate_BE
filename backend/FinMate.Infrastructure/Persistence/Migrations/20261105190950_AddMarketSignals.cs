using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_market_documents_status",
                table: "market_documents");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ai_attempted_at",
                table: "market_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "market_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    topic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_events", x => x.id);
                    table.CheckConstraint("chk_market_events_direction", "direction IN ('up','down','stable')");
                    table.CheckConstraint("chk_market_events_status", "status IN ('pending','approved','rejected','expired')");
                    table.CheckConstraint("chk_market_events_topic", "topic IN ('fuel','electricity','water','food_price','other')");
                    table.ForeignKey(
                        name: "FK_market_events_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "market_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fact_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    change_percent = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_signals", x => x.id);
                    table.CheckConstraint("chk_market_signals_direction", "direction IN ('up','down','stable')");
                    table.CheckConstraint("chk_market_signals_fact_status", "fact_status IN ('actual','forecast')");
                    table.CheckConstraint("chk_market_signals_topic", "topic IN ('fuel','electricity','water','food_price','other')");
                    table.ForeignKey(
                        name: "FK_market_signals_market_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "market_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_documents_status",
                table: "market_documents",
                sql: "status IN ('pending_ai','processed','ai_not_allowed','no_content','blocked','failed')");

            migrationBuilder.CreateIndex(
                name: "IX_market_events_reviewed_by_user_id",
                table: "market_events",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_events_status",
                table: "market_events",
                columns: new[] { "status", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "uq_market_events_one_open",
                table: "market_events",
                columns: new[] { "topic", "direction" },
                unique: true,
                filter: "status IN ('pending', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_market_signals_document",
                table: "market_signals",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_signals_topic",
                table: "market_signals",
                columns: new[] { "topic", "direction", "fact_status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_events");

            migrationBuilder.DropTable(
                name: "market_signals");

            migrationBuilder.DropCheckConstraint(
                name: "chk_market_documents_status",
                table: "market_documents");

            migrationBuilder.DropColumn(
                name: "ai_attempted_at",
                table: "market_documents");

            // Giá trị mới không hợp lệ với CHECK cũ. Bài quay về trạng thái chờ, không mất dòng.
            migrationBuilder.Sql("UPDATE market_documents SET status = 'pending_ai' WHERE status IN ('processed', 'ai_not_allowed');");

            migrationBuilder.AddCheckConstraint(
                name: "chk_market_documents_status",
                table: "market_documents",
                sql: "status IN ('pending_ai','no_content','blocked','failed')");
        }
    }
}
