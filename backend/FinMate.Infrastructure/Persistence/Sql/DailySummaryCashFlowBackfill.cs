namespace FinMate.Infrastructure.Persistence.Sql;

/// <summary>
/// Phase 90 (Q16): điền các cột dòng tiền của <c>daily_summaries</c> đã chốt trước khi có chúng. Cùng quy tắc với <c>CashFlowCalculator</c> cho mọi ví
/// (chuyển ví nội bộ bù về 0; vay nợ theo danh mục <c>loan</c>; quỹ theo hướng; cặp đảo riêng), ngày theo giờ VN (+07, không đổi giờ).
/// Chạy MỘT lần trong migration <c>AddReportCashFlow</c>; làm mọi dòng đã có (không chỉ 90 ngày) vì chỉ là một lượt gom trên <c>transactions</c>.
/// Ngày chưa có dòng summary thì báo cáo vẫn tính live như trước.
/// </summary>
public static class DailySummaryCashFlowBackfill
{
    public const string Sql = """
        WITH rows AS (
            SELECT t.user_id,
                   ((t.transacted_at AT TIME ZONE 'UTC') + interval '7 hours')::date AS day,
                   t.transaction_type AS type, t.amount_cents AS amount, t.counter_account_id AS counter,
                   t.group_fund_entry_id IS NOT NULL AS in_fund, t.group_fund_direction AS fund_direction,
                   t.relation, t.is_reversed, COALESCE(c.slug = 'loan', false) AS is_loan
            FROM transactions t
            LEFT JOIN categories c ON c.id = t.category_id
            WHERE t.status = 'confirmed' AND t.deleted_at IS NULL
        ), sums AS (
            SELECT user_id, day,
                   SUM(CASE
                         WHEN type = 'debit' THEN -amount
                         WHEN type = 'credit' THEN amount
                         WHEN type = 'transfer' AND in_fund AND fund_direction = 'in' THEN amount
                         WHEN type = 'transfer' AND in_fund THEN -amount
                         ELSE 0 END) AS net,
                   SUM(CASE WHEN NOT in_fund AND type <> 'transfer' AND is_loan AND type = 'debit' THEN amount ELSE 0 END) AS loan_out,
                   SUM(CASE WHEN NOT in_fund AND type <> 'transfer' AND is_loan AND type = 'credit' THEN amount ELSE 0 END) AS loan_in,
                   SUM(CASE WHEN in_fund AND fund_direction = 'out' THEN amount ELSE 0 END) AS fund_out,
                   SUM(CASE WHEN in_fund AND fund_direction = 'in' THEN amount ELSE 0 END) AS fund_in,
                   SUM(CASE
                         WHEN in_fund OR type = 'transfer' OR is_loan THEN 0
                         WHEN type = 'debit' AND is_reversed THEN -amount
                         WHEN type = 'credit' AND relation = 'reversal' THEN amount
                         ELSE 0 END) AS reversal_net
            FROM rows
            GROUP BY user_id, day
        )
        UPDATE daily_summaries s
        SET cash_flow_net_cents = sums.net,
            loan_outflow_cents = sums.loan_out,
            loan_inflow_cents = sums.loan_in,
            group_fund_outflow_cents = sums.fund_out,
            group_fund_inflow_cents = sums.fund_in,
            reversal_net_cents = sums.reversal_net
        FROM sums
        WHERE s.user_id = sums.user_id AND s.summary_date = sums.day;
        """;
}
