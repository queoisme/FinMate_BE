using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQrTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counterparty_category_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    account_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counterparty_category_overrides", x => x.id);
                    table.CheckConstraint("chk_counterparty_category_overrides_bin", "bank_bin ~ '^[0-9]{6}$'");
                    table.ForeignKey(
                        name: "FK_counterparty_category_overrides_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_counterparty_category_overrides_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qr_transfer_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    receiver_account_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    receiver_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sender_bank_bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    message = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    matched_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_qr_transfer_intents", x => x.id);
                    table.CheckConstraint("chk_qr_transfer_intents_amount", "amount_cents > 0");
                    table.CheckConstraint("chk_qr_transfer_intents_bins", "receiver_bank_bin ~ '^[0-9]{6}$' AND sender_bank_bin ~ '^[0-9]{6}$'");
                    table.CheckConstraint("chk_qr_transfer_intents_status", "status IN ('pending','matched','cancelled')");
                    table.ForeignKey(
                        name: "FK_qr_transfer_intents_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_qr_transfer_intents_transactions_matched_transaction_id",
                        column: x => x.matched_transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_qr_transfer_intents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vietqr_banks",
                columns: table => new
                {
                    bin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    deeplink_app_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vietqr_banks", x => x.bin);
                    table.CheckConstraint("chk_vietqr_banks_bin", "bin ~ '^[0-9]{6}$'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_counterparty_category_overrides_category_id",
                table: "counterparty_category_overrides",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "uq_counterparty_category_overrides_user_account",
                table: "counterparty_category_overrides",
                columns: new[] { "user_id", "bank_bin", "account_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_qr_transfer_intents_category_id",
                table: "qr_transfer_intents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_qr_transfer_intents_matched_transaction",
                table: "qr_transfer_intents",
                column: "matched_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_qr_transfer_intents_pending_match",
                table: "qr_transfer_intents",
                columns: new[] { "user_id", "sender_bank_bin", "amount_cents" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_qr_transfer_intents_user_scanned",
                table: "qr_transfer_intents",
                columns: new[] { "user_id", "scanned_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_vietqr_banks_provider_key",
                table: "vietqr_banks",
                column: "provider_key");

            migrationBuilder.CreateIndex(
                name: "uq_vietqr_banks_app_code",
                table: "vietqr_banks",
                column: "deeplink_app_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counterparty_category_overrides");

            migrationBuilder.DropTable(
                name: "qr_transfer_intents");

            migrationBuilder.DropTable(
                name: "vietqr_banks");
        }
    }
}
