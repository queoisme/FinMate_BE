namespace FinMate.Infrastructure.Persistence.Sql;

/// <summary>
/// SQL của dashboard KPI (Phase 94), dùng ở <c>KpiRepository</c>. Gom theo ngày VN bằng <c>(ts AT TIME ZONE 'UTC') + 7 giờ</c> (Việt Nam không đổi giờ).
/// Tài khoản thường = không có hàng <c>user_roles</c> nào khác <c>role_id = 0</c> (<c>UserRole.User</c>), giống cách <c>User.Role</c> đọc: thiếu hàng thì là User.
/// Giá trị trạng thái/nguồn là dạng lưu trong DB (chữ thường, snake_case). Chỉ trả số đếm hoặc tổng, không cột nào về từng người.
/// </summary>
public static class KpiSql
{
    /// <summary>
    /// Các chỉ số dòng chảy theo ngày VN trong [@from, @to). Tham số: <c>@from</c>, <c>@to</c>.
    /// <c>email_verified</c> và <c>onboarding_completed</c> theo ngày ĐĂNG KÝ (người đăng ký hôm đó hiện đã xác minh / đã hoàn thành), không theo ngày của mốc:
    /// Phase 17 và migration <c>AddFinanceMethodsAndOnboarding</c> đặt hai mốc này bằng giờ chạy migration cho mọi tài khoản cũ, đếm theo mốc sẽ thành một ngày tăng vọt giả.
    /// Onboarding chỉ tính trạng thái <c>completed</c>; <c>skipped</c> cũng có mốc thời gian nhưng không phải hoàn thành.
    /// </summary>
    public const string Daily = """
        WITH plain AS (
            SELECT u.id, u.created_at, u.email_verified_at, u.onboarding_status
            FROM users u
            WHERE NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = u.id AND ur.role_id <> 0)
        )
        SELECT x.day AS "Day", x.metric AS "Metric", x.key AS "Key", x.value AS "Value" FROM (
            SELECT ((created_at AT TIME ZONE 'UTC') + interval '7 hours')::date AS day, 'signups' AS metric, NULL::text AS key, count(*)::bigint AS value
            FROM plain WHERE created_at >= @from AND created_at < @to GROUP BY 1
            UNION ALL
            SELECT ((created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'email_verified', NULL, count(*)
            FROM plain WHERE created_at >= @from AND created_at < @to AND email_verified_at IS NOT NULL GROUP BY 1
            UNION ALL
            SELECT ((created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'onboarding_completed', NULL, count(*)
            FROM plain WHERE created_at >= @from AND created_at < @to AND onboarding_status = 'completed' GROUP BY 1
            UNION ALL
            SELECT ((f.first_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'method_chosen', NULL, count(*)
            FROM (SELECT um.user_id, min(um.created_at) AS first_at FROM user_methods um JOIN plain p ON p.id = um.user_id GROUP BY um.user_id) f
            WHERE f.first_at >= @from AND f.first_at < @to GROUP BY 1
            UNION ALL
            SELECT ((d.requested_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'deletion_requests', NULL, count(*)
            FROM data_deletion_requests d JOIN plain p ON p.id = d.user_id
            WHERE d.requested_at >= @from AND d.requested_at < @to GROUP BY 1
            UNION ALL
            SELECT ((t.created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'transactions', t.source, count(*)
            FROM transactions t JOIN plain p ON p.id = t.user_id
            WHERE t.status = 'confirmed' AND t.deleted_at IS NULL AND t.created_at >= @from AND t.created_at < @to GROUP BY 1, t.source
            UNION ALL
            SELECT ((n.created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'notifications', n.status, count(*)
            FROM notification_logs n JOIN plain p ON p.id = n.user_id
            WHERE n.created_at >= @from AND n.created_at < @to GROUP BY 1, n.status
            UNION ALL
            SELECT ((n.created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'notifications_converted', NULL, count(*)
            FROM notification_logs n JOIN plain p ON p.id = n.user_id
            WHERE n.created_at >= @from AND n.created_at < @to
              AND EXISTS (SELECT 1 FROM transactions t WHERE t.notification_log_id = n.id AND t.deleted_at IS NULL)
            GROUP BY 1
            UNION ALL
            SELECT ((o.paid_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'paid_orders', NULL, count(*)
            FROM subscription_orders o
            WHERE o.status = 'paid' AND o.paid_at >= @from AND o.paid_at < @to
              AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = o.user_id AND ur.role_id <> 0)
            GROUP BY 1
            UNION ALL
            SELECT ((o.paid_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'revenue', NULL, sum(o.amount_cents)::bigint
            FROM subscription_orders o
            WHERE o.status = 'paid' AND o.paid_at >= @from AND o.paid_at < @to
              AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = o.user_id AND ur.role_id <> 0)
            GROUP BY 1
            UNION ALL
            SELECT ((o.created_at AT TIME ZONE 'UTC') + interval '7 hours')::date, 'needs_review', NULL, count(*)
            FROM subscription_orders o
            WHERE o.status = 'needs_review' AND o.created_at >= @from AND o.created_at < @to
              AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = o.user_id AND ur.role_id <> 0)
            GROUP BY 1
        ) x
        """;

    /// <summary>Người khác nhau có giao dịch đã xác nhận, không từ thông báo, trong từng cửa sổ. Tham số: <c>@starts</c>, <c>@ends</c> (timestamptz[]).</summary>
    public const string TransactingUsers = """
        SELECT w.idx::int AS "Index", NULL::text AS "Key", count(DISTINCT t.user_id)::bigint AS "Value"
        FROM unnest(@starts::timestamptz[], @ends::timestamptz[]) WITH ORDINALITY AS w(s, e, idx)
        LEFT JOIN transactions t
          ON t.created_at >= w.s AND t.created_at < w.e
         AND t.status = 'confirmed' AND t.deleted_at IS NULL AND t.source <> 'notification'
         AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = t.user_id AND ur.role_id <> 0)
        GROUP BY w.idx
        """;

    /// <summary>Người đang trả phí tại từng thời điểm; dòng có <c>Key</c> null là tổng. Tham số: <c>@at</c> (timestamptz[]).</summary>
    public const string Paying = """
        SELECT w.idx::int AS "Index", sp.plan_code AS "Key", count(DISTINCT sp.user_id)::bigint AS "Value"
        FROM unnest(@at::timestamptz[]) WITH ORDINALITY AS w(at, idx)
        JOIN user_subscription_periods sp ON sp.starts_at <= w.at AND sp.ends_at > w.at AND sp.voided_at IS NULL
        JOIN users u ON u.id = sp.user_id AND u.deleted_at IS NULL
        WHERE NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = sp.user_id AND ur.role_id <> 0)
        GROUP BY GROUPING SETS ((w.idx, sp.plan_code), (w.idx))
        """;

    /// <summary>Ảnh chụp hiện tại số người dùng ngân sách, mục tiêu, sổ vay, nhóm; bỏ tài khoản chờ xoá.</summary>
    public const string FeatureUsers = """
        WITH active AS (
            SELECT u.id FROM users u
            WHERE u.deleted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM user_roles ur WHERE ur.user_id = u.id AND ur.role_id <> 0)
        )
        SELECT
            (SELECT count(DISTINCT b.user_id) FROM budgets b JOIN active a ON a.id = b.user_id WHERE b.deleted_at IS NULL)::bigint AS "Budgets",
            (SELECT count(DISTINCT g.user_id) FROM saving_goals g JOIN active a ON a.id = g.user_id
              WHERE g.deleted_at IS NULL AND g.status IN ('draft', 'active'))::bigint AS "Goals",
            (SELECT count(DISTINCT l.user_id) FROM loans l JOIN active a ON a.id = l.user_id
              WHERE l.deleted_at IS NULL AND l.status = 'open')::bigint AS "Loans",
            (SELECT count(DISTINCT gm.user_id) FROM group_members gm JOIN active a ON a.id = gm.user_id
              JOIN groups gr ON gr.id = gm.group_id AND gr.deleted_at IS NULL
              WHERE gm.left_at IS NULL)::bigint AS "Groups"
        """;
}
