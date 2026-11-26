namespace FinMate.Application.Kpi;

/// <summary>Kết quả <c>GET /ops/kpi/summary</c>. Chỉ có số đếm, tỉ lệ và tổng; không id, email, tên hay số tiền của từng người (BR-KPI-01).</summary>
public sealed record KpiSummaryDto(
    string Granularity,
    int MinCellSize,
    KpiFeatureUsersDto FeatureUsers,
    KpiNotesDto Notes,
    IReadOnlyList<KpiPeriodDto> Periods);

/// <summary>Ảnh chụp lúc <see cref="AsOf"/>, không theo kỳ (D6).</summary>
public sealed record KpiFeatureUsersDto(long Budgets, long Goals, long Loans, long Groups, DateTimeOffset AsOf);

/// <summary><see cref="ManualIncludesChat"/>: giao dịch từ chat và giọng nói qua server được lưu với nguồn <c>manual</c>.</summary>
public sealed record KpiNotesDto(bool ManualIncludesChat);

public sealed record KpiPeriodDto(
    DateOnly Start,
    DateOnly End,
    bool Partial,
    long Signups,
    long EmailVerified,
    long OnboardingCompleted,
    long MethodChosen,
    long DeletionRequests,
    long TransactingUsers,
    KpiTransactionsDto Transactions,
    KpiNotificationsDto Notifications,
    KpiSubscriptionsDto Subscriptions);

public sealed record KpiTransactionsDto(long Total, IReadOnlyDictionary<string, KpiCount> BySource);

public sealed record KpiNotificationsDto(long Received, long Ignored, long Duplicate, long Converted, double? ConversionRate);

/// <summary><see cref="RevenueCents"/>: tổng tiền (đồng) của đơn đã trả trong kỳ; không có doanh thu theo gói (D9).</summary>
public sealed record KpiSubscriptionsDto(
    long PayingTotal,
    IReadOnlyDictionary<string, KpiCount> PayingByPlan,
    long PaidOrders,
    long RevenueCents,
    long NeedsReview);
