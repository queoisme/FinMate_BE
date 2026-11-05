using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportConsentedAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions");

            migrationBuilder.AddColumn<Guid>(
                name: "support_ticket_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "support_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scopes = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_access_grants", x => x.id);
                    table.CheckConstraint("chk_support_access_grants_scopes", "scopes > 0");
                    table.CheckConstraint("chk_support_access_grants_status", "status IN ('pending','approved','denied','revoked')");
                    table.ForeignKey(
                        name: "FK_support_access_grants_financial_accounts_financial_account_~",
                        column: x => x.financial_account_id,
                        principalTable: "financial_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_support_access_grants_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_access_grants_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "support_data_access_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_data_access_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_support_data_access_logs_support_access_grants_grant_id",
                        column: x => x.grant_id,
                        principalTable: "support_access_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_data_access_logs_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_data_access_logs_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_support_data_access_logs_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_support_ticket_id",
                table: "transactions",
                column: "support_ticket_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions",
                sql: "source IN ('notification','manual','voice','receipt','support')");

            migrationBuilder.CreateIndex(
                name: "IX_support_access_grants_financial_account_id",
                table: "support_access_grants",
                column: "financial_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_access_grants_requested_by_user_id",
                table: "support_access_grants",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_access_grants_ticket",
                table: "support_access_grants",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "uq_support_access_grants_one_pending",
                table: "support_access_grants",
                column: "ticket_id",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_support_data_access_logs_actor_user_id",
                table: "support_data_access_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_data_access_logs_grant_id",
                table: "support_data_access_logs",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_data_access_logs_ticket_id",
                table: "support_data_access_logs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_data_access_logs_customer",
                table: "support_data_access_logs",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_support_tickets_support_ticket_id",
                table: "transactions",
                column: "support_ticket_id",
                principalTable: "support_tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bản cũ không có nguồn này; không đổi thì ràng buộc cũ không thêm lại được.
            migrationBuilder.Sql("UPDATE transactions SET source = 'manual' WHERE source = 'support';");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_support_tickets_support_ticket_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "support_data_access_logs");

            migrationBuilder.DropTable(
                name: "support_access_grants");

            migrationBuilder.DropIndex(
                name: "IX_transactions_support_ticket_id",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "support_ticket_id",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "chk_transactions_source",
                table: "transactions",
                sql: "source IN ('notification','manual','voice','receipt')");
        }
    }
}
