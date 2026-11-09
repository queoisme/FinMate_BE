using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "community_area_code",
                table: "users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "community_consent_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "community_consent_version",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "community_muted_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "community_prompts_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "community_province_code",
                table: "users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "community_places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    province_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    area_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    address_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_places", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "community_posts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    topic = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_cents = table.Column<long>(type: "bigint", nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    spent_here_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_posts", x => x.id);
                    table.CheckConstraint("chk_community_posts_place_fields", "place_id IS NOT NULL OR (price_cents IS NULL AND NOT spent_here_verified)");
                    table.CheckConstraint("chk_community_posts_price", "price_cents IS NULL OR price_cents > 0");
                    table.CheckConstraint("chk_community_posts_status", "status IN ('visible','auto_hidden','removed')");
                    table.ForeignKey(
                        name: "FK_community_posts_community_places_place_id",
                        column: x => x.place_id,
                        principalTable: "community_places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_community_posts_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "community_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_comments", x => x.id);
                    table.CheckConstraint("chk_community_comments_status", "status IN ('visible','auto_hidden','removed')");
                    table.ForeignKey(
                        name: "FK_community_comments_community_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "community_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "community_post_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_post_images", x => x.id);
                    table.CheckConstraint("chk_community_post_images_status", "status IN ('pending','approved','rejected','deleted')");
                    table.ForeignKey(
                        name: "FK_community_post_images_community_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "community_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_post_images_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "community_post_votes",
                columns: table => new
                {
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_post_votes", x => new { x.post_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_community_post_votes_community_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "community_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_post_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "community_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_reports", x => x.id);
                    table.CheckConstraint("chk_community_reports_priority", "priority IN ('normal','urgent')");
                    table.CheckConstraint("chk_community_reports_reason", "reason IN ('scam','personal_info','harassment','sexual','spam','self_harm','other')");
                    table.CheckConstraint("chk_community_reports_status", "status IN ('open','upheld','dismissed','closed')");
                    table.CheckConstraint("chk_community_reports_target", "(post_id IS NULL) <> (comment_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_community_reports_community_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "community_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_reports_community_posts_post_id",
                        column: x => x.post_id,
                        principalTable: "community_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_reports_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_community_comments_author_created",
                table: "community_comments",
                columns: new[] { "author_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_community_comments_post_created",
                table: "community_comments",
                columns: new[] { "post_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_community_places_area_name",
                table: "community_places",
                columns: new[] { "province_code", "area_code", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_community_post_images_reviewed_by",
                table: "community_post_images",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_community_post_images_post",
                table: "community_post_images",
                column: "post_id");

            migrationBuilder.CreateIndex(
                name: "ix_community_post_images_queue",
                table: "community_post_images",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_community_post_votes_user_created",
                table: "community_post_votes",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_community_posts_author_created",
                table: "community_posts",
                columns: new[] { "author_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_community_posts_created",
                table: "community_posts",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_community_posts_place",
                table: "community_posts",
                column: "place_id",
                filter: "place_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_community_posts_topic_created",
                table: "community_posts",
                columns: new[] { "topic", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_community_posts_author_place",
                table: "community_posts",
                columns: new[] { "author_id", "place_id" },
                unique: true,
                filter: "place_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_community_reports_resolved_by",
                table: "community_reports",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_community_reports_queue",
                table: "community_reports",
                columns: new[] { "status", "priority", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_community_reports_reporter_created",
                table: "community_reports",
                columns: new[] { "reporter_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_community_reports_comment_reporter",
                table: "community_reports",
                columns: new[] { "comment_id", "reporter_id" },
                unique: true,
                filter: "comment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_community_reports_post_reporter",
                table: "community_reports",
                columns: new[] { "post_id", "reporter_id" },
                unique: true,
                filter: "post_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community_post_images");

            migrationBuilder.DropTable(
                name: "community_post_votes");

            migrationBuilder.DropTable(
                name: "community_reports");

            migrationBuilder.DropTable(
                name: "community_comments");

            migrationBuilder.DropTable(
                name: "community_posts");

            migrationBuilder.DropTable(
                name: "community_places");

            migrationBuilder.DropColumn(
                name: "community_area_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "community_consent_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "community_consent_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "community_muted_until",
                table: "users");

            migrationBuilder.DropColumn(
                name: "community_prompts_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "community_province_code",
                table: "users");
        }
    }
}
