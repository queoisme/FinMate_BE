using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinMate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportCashFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cash_flow_net_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "group_fund_inflow_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "group_fund_outflow_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "loan_inflow_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "loan_outflow_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "reversal_net_cents",
                table: "daily_summaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        
            // Phase 90 (Q16): các ngày đã chốt có dòng tiền ngay, không phải chờ job.
            migrationBuilder.Sql(FinMate.Infrastructure.Persistence.Sql.DailySummaryCashFlowBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_flow_net_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "group_fund_inflow_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "group_fund_outflow_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "loan_inflow_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "loan_outflow_cents",
                table: "daily_summaries");

            migrationBuilder.DropColumn(
                name: "reversal_net_cents",
                table: "daily_summaries");
        }
    }
}
