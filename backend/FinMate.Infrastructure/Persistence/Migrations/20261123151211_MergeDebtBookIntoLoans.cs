using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeDebtBookIntoLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_principal",
                table: "loans");

            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "loans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "loans",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "repaid_cents",
                table: "loans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "settled_at",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "loans",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "open");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "written_off_at",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "loan_repayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "loan_repayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "interest_transaction_id",
                table: "loan_repayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "loan_repayments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "repayment");

            // Phase 90 (Q5): chuyển dữ liệu trước khi thêm ràng buộc. Dữ liệu không thể tạo qua code cũ thì dừng, không tự sửa.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT transaction_id FROM loan_repayments WHERE transaction_id IS NOT NULL GROUP BY transaction_id HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'MergeDebtBookIntoLoans: có giao dịch gắn với hơn một lần trả; sửa tay trước khi chạy migration';
                  END IF;
                  IF EXISTS (SELECT 1 FROM loan_repayments r WHERE r.transaction_id IS NOT NULL
                             AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.id = r.transaction_id)) THEN
                    RAISE EXCEPTION 'MergeDebtBookIntoLoans: có lần trả trỏ tới giao dịch không còn tồn tại; sửa tay trước khi chạy migration';
                  END IF;
                  IF EXISTS (SELECT 1 FROM loans l WHERE l.principal_cents < (SELECT coalesce(sum(r.amount_cents), 0) FROM loan_repayments r WHERE r.loan_id = l.id)) THEN
                    RAISE EXCEPTION 'MergeDebtBookIntoLoans: có khoản đã trả nhiều hơn gốc; sửa tay trước khi chạy migration';
                  END IF;
                END $$;

                INSERT INTO loan_repayments (id, loan_id, kind, amount_cents, paid_at, transaction_id, created_at)
                SELECT gen_random_uuid(), l.id, 'principal', l.principal_cents, (l.start_date::timestamp - interval '7 hours') AT TIME ZONE 'UTC', NULL, l.created_at
                FROM loans l;

                UPDATE loans l
                SET repaid_cents = s.repaid,
                    status = CASE WHEN s.repaid >= l.principal_cents THEN 'settled' ELSE 'open' END,
                    settled_at = CASE WHEN s.repaid >= l.principal_cents THEN l.updated_at ELSE NULL END
                FROM (SELECT l2.id, coalesce(sum(r.amount_cents) FILTER (WHERE r.kind = 'repayment'), 0) AS repaid
                      FROM loans l2 LEFT JOIN loan_repayments r ON r.loan_id = l2.id GROUP BY l2.id) s
                WHERE s.id = l.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_loans_user_status_due",
                table: "loans",
                columns: new[] { "user_id", "status", "due_date" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_loans_user_client_request",
                table: "loans",
                columns: new[] { "user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_principal",
                table: "loans",
                sql: "principal_cents >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_repaid",
                table: "loans",
                sql: "repaid_cents >= 0 AND principal_cents >= repaid_cents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_settled_at",
                table: "loans",
                sql: "(status = 'settled') = (settled_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_status",
                table: "loans",
                sql: "status IN ('open','settled','written_off')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_written_off_at",
                table: "loans",
                sql: "(status = 'written_off') = (written_off_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_loan_repayments_client_request",
                table: "loan_repayments",
                columns: new[] { "loan_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_loan_repayments_interest_transaction",
                table: "loan_repayments",
                column: "interest_transaction_id",
                unique: true,
                filter: "interest_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_loan_repayments_transaction",
                table: "loan_repayments",
                column: "transaction_id",
                unique: true,
                filter: "transaction_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loan_repayments_interest_only_on_repayment",
                table: "loan_repayments",
                sql: "interest_transaction_id IS NULL OR kind = 'repayment'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loan_repayments_kind",
                table: "loan_repayments",
                sql: "kind IN ('principal','repayment')");

            migrationBuilder.AddForeignKey(
                name: "fk_loan_repayments_interest_transaction",
                table: "loan_repayments",
                column: "interest_transaction_id",
                principalTable: "transactions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_loan_repayments_transaction",
                table: "loan_repayments",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id");

            // Hai tầng cascade (users → loans → loan_repayments) chạy SAU khi transactions đã bị xoá, nên NO ACTION thường báo lỗi khi xoá cứng
            // người dùng. Kiểm lúc commit thì cả sổ đã đi theo; xoá cứng riêng một giao dịch đang gắn vẫn bị chặn. EF không biểu diễn DEFERRABLE:
            // migration sau này mà tạo lại hai FK này phải giữ dòng ALTER này (LoanBookSchemaTests.HardDeletingTheUser_… sẽ đỏ nếu quên).
            migrationBuilder.Sql("""
                ALTER TABLE loan_repayments ALTER CONSTRAINT fk_loan_repayments_transaction DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE loan_repayments ALTER CONSTRAINT fk_loan_repayments_interest_transaction DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trước khi bỏ cột kind: dòng giải ngân không phải lần trả; khoản / dòng đã xoá mềm không có chỗ trong lược đồ cũ.
            migrationBuilder.Sql("""
                DELETE FROM loan_repayments WHERE kind = 'principal' OR deleted_at IS NOT NULL;
                DELETE FROM loans WHERE deleted_at IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_loan_repayments_interest_transaction",
                table: "loan_repayments");

            migrationBuilder.DropForeignKey(
                name: "fk_loan_repayments_transaction",
                table: "loan_repayments");

            migrationBuilder.DropIndex(
                name: "ix_loans_user_status_due",
                table: "loans");

            migrationBuilder.DropIndex(
                name: "ux_loans_user_client_request",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_principal",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_repaid",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_settled_at",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_status",
                table: "loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loans_written_off_at",
                table: "loans");

            migrationBuilder.DropIndex(
                name: "ux_loan_repayments_client_request",
                table: "loan_repayments");

            migrationBuilder.DropIndex(
                name: "ux_loan_repayments_interest_transaction",
                table: "loan_repayments");

            migrationBuilder.DropIndex(
                name: "ux_loan_repayments_transaction",
                table: "loan_repayments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loan_repayments_interest_only_on_repayment",
                table: "loan_repayments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_loan_repayments_kind",
                table: "loan_repayments");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "note",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "repaid_cents",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "settled_at",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "status",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "written_off_at",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "loan_repayments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "loan_repayments");

            migrationBuilder.DropColumn(
                name: "interest_transaction_id",
                table: "loan_repayments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "loan_repayments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_loans_principal",
                table: "loans",
                sql: "principal_cents > 0");
        }
    }
}
