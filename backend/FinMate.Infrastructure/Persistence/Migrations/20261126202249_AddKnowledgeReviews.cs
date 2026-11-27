using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>Phase 95: người gửi và người duyệt tài liệu tri thức (AI Service không nhận id người gửi), và mốc bắt đầu ghi.</summary>
    public partial class AddKnowledgeReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_review_cutover",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    tracked_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_review_cutover", x => x.id);
                    table.CheckConstraint("chk_knowledge_review_cutover_single", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "knowledge_reviews",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_reviews", x => x.document_id);
                    table.CheckConstraint("chk_knowledge_reviews_status", "status IN ('draft','approved','rejected','retired')");
                    table.CheckConstraint("chk_knowledge_reviews_two_people", "decided_by IS NULL OR submitted_by IS NULL OR decided_by <> submitted_by");
                });

            // S3: mốc backend bắt đầu ghi người soạn = giờ chạy migration này. Tài liệu tạo ở AI Service trước mốc là "cũ" (ai có quyền cũng duyệt được);
            // tài liệu tạo sau mốc mà không có dòng knowledge_reviews thì không duyệt được.
            migrationBuilder.Sql("INSERT INTO knowledge_review_cutover (id, tracked_since) VALUES (1, now()) ON CONFLICT (id) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_review_cutover");

            migrationBuilder.DropTable(
                name: "knowledge_reviews");
        }
    }
}
