using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ops_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    related_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_notifications", x => x.id);
                    table.CheckConstraint("chk_ops_notifications_type", "type IN ('ticket_new','ticket_assigned','ticket_escalated','approval_pending','approval_decided','incident_opened','community_report','community_image_pending','data_deletion_requested')");
                    table.ForeignKey(
                        name: "FK_ops_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ops_notifications_unread",
                table: "ops_notifications",
                columns: new[] { "user_id", "type", "related_entity_id" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ops_notifications_user_created",
                table: "ops_notifications",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_notifications");
        }
    }
}
