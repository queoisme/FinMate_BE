using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    first_response_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolve_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.id);
                    table.CheckConstraint("chk_support_tickets_category", "category IN ('security','account','transaction','receipt','notification','group','question')");
                    table.CheckConstraint("chk_support_tickets_priority", "priority IN ('p1','p2','p3')");
                    table.CheckConstraint("chk_support_tickets_rating", "rating IS NULL OR rating BETWEEN 1 AND 5");
                    table.CheckConstraint("chk_support_tickets_status", "status IN ('new','in_progress','waiting_customer','escalated','resolved','closed')");
                    table.ForeignKey(
                        name: "FK_support_tickets_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_support_tickets_users_requester_user_id",
                        column: x => x.requester_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket_messages", x => x.id);
                    table.CheckConstraint("chk_support_ticket_messages_kind", "kind IN ('customer','staff','internal_note')");
                    table.ForeignKey(
                        name: "FK_support_ticket_messages_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_ticket_messages_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    delete_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_support_ticket_attachments_support_ticket_messages_message_~",
                        column: x => x.message_id,
                        principalTable: "support_ticket_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_support_ticket_attachments_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_attachments_ticket_id",
                table: "support_ticket_attachments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_delete_after",
                table: "support_ticket_attachments",
                column: "delete_after",
                filter: "delete_after IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_message",
                table: "support_ticket_attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_messages_author_user_id",
                table: "support_ticket_messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_messages_ticket",
                table: "support_ticket_messages",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_assignee",
                table: "support_tickets",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_queue",
                table: "support_tickets",
                columns: new[] { "status", "resolve_due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_requester",
                table: "support_tickets",
                columns: new[] { "requester_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_ticket_attachments");

            migrationBuilder.DropTable(
                name: "support_ticket_messages");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
