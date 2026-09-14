using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_tokens", x => x.id);
                    table.CheckConstraint("chk_device_tokens_platform", "platform IN ('android','ios','web')");
                    table.ForeignKey(
                        name: "FK_device_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_device_tokens_user_id",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_device_tokens_token",
                table: "device_tokens",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_tokens");
        }
    }
}
