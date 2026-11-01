using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceMethodHomeLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "home_layout",
                table: "finance_methods",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: "chk_finance_methods_home_layout",
                table: "finance_methods",
                sql: "jsonb_typeof(home_layout) = 'array'");

            // Hàng đã có (cột vừa thêm mang '[]') nhận cùng bố cục với seed mới. Literal cố ý nằm ở đây thay vì tham chiếu hằng trong code: migration phải giữ nguyên nghĩa dù code đổi.
            migrationBuilder.Sql(
                """
                UPDATE finance_methods SET home_layout = '[{"type":"spendable"},{"type":"params"},{"type":"goals"},{"type":"starter_steps"}]'::jsonb WHERE home_layout = '[]'::jsonb;
                UPDATE finance_methods SET home_layout = '[{"type":"params"},{"type":"starter_steps"}]'::jsonb WHERE code IN ('minimal_tracking','debt_repayment');
                UPDATE finance_methods SET home_layout = '[{"type":"spendable"},{"type":"params","title":"Chia thu nhập","keys":["needs_pct","wants_pct","goals_pct"],"display":"allocation"},{"type":"params","title":"Ngưỡng cảnh báo","keys":["t1_pct","t2_pct"]},{"type":"goals"}]'::jsonb WHERE code = 'category_budget';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_finance_methods_home_layout",
                table: "finance_methods");

            migrationBuilder.DropColumn(
                name: "home_layout",
                table: "finance_methods");
        }
    }
}
