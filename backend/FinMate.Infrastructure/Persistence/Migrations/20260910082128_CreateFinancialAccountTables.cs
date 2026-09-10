using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateFinancialAccountTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_configs", x => x.id);
                    table.CheckConstraint("chk_provider_configs_account_type", "account_type IN ('bank','ewallet')");
                });

            migrationBuilder.CreateTable(
                name: "financial_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    account_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_monitored = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    balance_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_accounts", x => x.id);
                    table.CheckConstraint("chk_financial_accounts_account_type", "account_type IN ('bank','ewallet','cash')");
                    table.ForeignKey(
                        name: "FK_financial_accounts_provider_configs_provider_config_id",
                        column: x => x.provider_config_id,
                        principalTable: "provider_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_provider_config_id",
                table: "financial_accounts",
                column: "provider_config_id");

            migrationBuilder.CreateIndex(
                name: "idx_financial_accounts_user_id",
                table: "financial_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_financial_accounts_user_package",
                table: "financial_accounts",
                columns: new[] { "user_id", "package_name" },
                unique: true,
                filter: "package_name IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_provider_configs_package_name",
                table: "provider_configs",
                column: "package_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_provider_configs_provider_key",
                table: "provider_configs",
                column: "provider_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_accounts");

            migrationBuilder.DropTable(
                name: "provider_configs");
        }
    }
}
