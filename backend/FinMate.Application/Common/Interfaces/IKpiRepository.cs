namespace FinMate.Application.Common.Interfaces;

/// <summary>Tên chỉ số trong <see cref="KpiDailyRow.Metric"/>.</summary>
public static class KpiMetrics
{
    public const string Signups = "signups";
    public const string EmailVerified = "email_verified";
    public const string OnboardingCompleted = "onboarding_completed";
    public const string MethodChosen = "method_chosen";
    public const string DeletionRequests = "deletion_requests";

    /// <summary><see cref="KpiDailyRow.Key"/> là nguồn (giá trị cột <c>transactions.source</c>).</summary>
    public const string Transactions = "transactions";

    /// <summary><see cref="KpiDailyRow.Key"/> là trạng thái (giá trị cột <c>notification_logs.status</c>).</summary>
    public const string Notifications = "notifications";
    public const string NotificationsConverted = "notifications_converted";
    public const string PaidOrders = "paid_orders";

    /// <summary>Tổng <c>amount_cents</c> (đơn vị đồng) của đơn đã trả.</summary>
    public const string Revenue = "revenue";
    public const string NeedsReview = "needs_review";
}

/// <summary>Một số đếm của một ngày VN. Không có cột nào về người dùng.</summary>
public sealed record KpiDailyRow(DateOnly Day, string Metric, string? Key, long Value);

/// <summary>Nửa mở [Start, End), offset 0.</summary>
public sealed record KpiTimeWindow(DateTimeOffset Start, DateTimeOffset End);

public sealed record KpiPayingSnapshot(long Total, IReadOnlyDictionary<string, long> ByPlan);

public sealed record KpiFeatureUserCounts(long Budgets, long Goals, long Loans, long Groups);

/// <summary>
/// Số liệu tổng hợp cho dashboard KPI (Phase 94). Mọi truy vấn chỉ trả số đếm hoặc tổng, chỉ tính tài khoản vai trò <c>User</c> (D17),
/// và tính khi gọi từ bảng sẵn có — không có bảng tổng hợp riêng.
/// </summary>
public interface IKpiRepository
{
    /// <summary>Các chỉ số dòng chảy, gom theo ngày VN của mốc thời gian của từng chỉ số, trong [from, to).</summary>
    Task<IReadOnlyList<KpiDailyRow>> GetDailyCountsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Số người khác nhau có giao dịch đã xác nhận, không từ thông báo, theo <c>created_at</c>, trong từng cửa sổ. Cùng thứ tự với <paramref name="windows"/>.</summary>
    Task<IReadOnlyList<long>> CountTransactingUsersAsync(IReadOnlyList<KpiTimeWindow> windows, CancellationToken ct = default);

    /// <summary>Người đang trả phí tại từng thời điểm (kỳ thuê bao chứa thời điểm, chưa bị huỷ), không tính tài khoản chờ xoá. Cùng thứ tự với <paramref name="instants"/>.</summary>
    Task<IReadOnlyList<KpiPayingSnapshot>> CountPayingAsync(IReadOnlyList<DateTimeOffset> instants, CancellationToken ct = default);

    /// <summary>Ảnh chụp hiện tại: số người đang dùng ngân sách, mục tiêu, sổ vay, nhóm. Không tính tài khoản chờ xoá.</summary>
    Task<KpiFeatureUserCounts> CountFeatureUsersAsync(CancellationToken ct = default);
}
