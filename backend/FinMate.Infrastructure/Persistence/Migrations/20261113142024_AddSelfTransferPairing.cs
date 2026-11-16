using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfTransferPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transfer_dismissed_at",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_kind",
                table: "ai_results",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transaction_merge_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survivor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transfer_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_merge_snapshots", x => x.id);
                    table.CheckConstraint("chk_transaction_merge_snapshots_before", "jsonb_typeof(before) = 'object'");
                    table.CheckConstraint("chk_transaction_merge_snapshots_kind", "kind IN ('duplicate_merge','transfer_pair','transfer_convert')");
                    table.CheckConstraint("chk_transaction_merge_snapshots_transfer_kind", "transfer_kind IS NULL OR transfer_kind IN ('atm_withdrawal','atm_deposit','wallet_topup','wallet_withdrawal','own_account')");
                    table.ForeignKey(
                        name: "FK_transaction_merge_snapshots_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies",
                sql: "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious','refunded','reversed','transfer')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results",
                sql: "relation_hint IS NULL OR relation_hint IN ('refund','reversal','transfer')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_transfer_kind",
                table: "ai_results",
                sql: "transfer_kind IS NULL OR (relation_hint = 'transfer' AND transfer_kind IN ('atm_withdrawal','atm_deposit','wallet_topup','wallet_withdrawal','own_account'))");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_merge_snapshots_created_at",
                table: "transaction_merge_snapshots",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_merge_snapshots_user_survivor",
                table: "transaction_merge_snapshots",
                columns: new[] { "user_id", "survivor_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_merge_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results");

            migrationBuilder.DropCheckConstraint(
                name: "chk_ai_results_transfer_kind",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "transfer_dismissed_at",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "transfer_kind",
                table: "ai_results");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transaction_anomalies_resolution",
                table: "transaction_anomalies",
                sql: "resolution IS NULL OR resolution IN ('normal','one_off','wrong_entry','suspicious','refunded','reversed')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_ai_results_relation_hint",
                table: "ai_results",
                sql: "relation_hint IS NULL OR relation_hint IN ('refund','reversal')");
        }
    }
}
