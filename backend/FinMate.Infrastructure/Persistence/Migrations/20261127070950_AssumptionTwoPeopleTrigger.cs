using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 95: đổi luật "người duyệt / từ chối khác người soạn" của <c>economic_assumptions</c> từ CHECK NOT VALID sang trigger. Postgres kiểm CHECK (kể cả
    /// NOT VALID) ở MỌI lần UPDATE, nên dòng tự duyệt có từ trước Phase 95 không gỡ được nữa. Trigger chỉ kiểm khi dòng được thêm hoặc khi
    /// <c>approved_by</c> / <c>rejected_by</c> đổi, và báo lỗi với cùng SQLSTATE 23514 và cùng tên ràng buộc như CHECK cũ. Miễn trừ tài khoản hệ thống của job CPI.
    /// </summary>
    public partial class AssumptionTwoPeopleTrigger : Migration
    {
        private const string Function = """
            CREATE OR REPLACE FUNCTION economic_assumptions_two_people() RETURNS trigger AS $$
            BEGIN
                IF NEW.approved_by IS NOT NULL AND NEW.approved_by = NEW.created_by
                   AND NEW.created_by <> '00000000-0000-0000-0000-00000000a1c5'
                   AND (TG_OP = 'INSERT' OR NEW.approved_by IS DISTINCT FROM OLD.approved_by) THEN
                    RAISE EXCEPTION 'approver must differ from author' USING ERRCODE = '23514', CONSTRAINT = 'chk_economic_assumptions_two_people';
                END IF;
                IF NEW.rejected_by IS NOT NULL AND NEW.rejected_by = NEW.created_by
                   AND (TG_OP = 'INSERT' OR NEW.rejected_by IS DISTINCT FROM OLD.rejected_by) THEN
                    RAISE EXCEPTION 'rejecter must differ from author' USING ERRCODE = '23514', CONSTRAINT = 'chk_economic_assumptions_two_people';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_economic_assumptions_two_people",
                table: "economic_assumptions");

            migrationBuilder.Sql(Function);
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_economic_assumptions_two_people BEFORE INSERT OR UPDATE OF approved_by, rejected_by ON economic_assumptions "
                + "FOR EACH ROW EXECUTE FUNCTION economic_assumptions_two_people();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_economic_assumptions_two_people ON economic_assumptions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS economic_assumptions_two_people();");
            migrationBuilder.Sql(
                "ALTER TABLE economic_assumptions ADD CONSTRAINT chk_economic_assumptions_two_people CHECK "
                + "((approved_by IS NULL OR approved_by <> created_by OR created_by = '00000000-0000-0000-0000-00000000a1c5') AND (rejected_by IS NULL OR rejected_by <> created_by)) NOT VALID;");
        }
    }
}
