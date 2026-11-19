namespace FinMate.Application.Common.Models;

public record MonthlySummaryDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalSpentCents,
    long TotalIncomeCents,
    long NetCents,
    int TransactionCount,
    long PrevMonthSpentCents,
    double? ChangePercent,
    long RefundedCents = 0,
    long? CrossPeriodRefundCents = null,
    long PrevMonthIncomeCents = 0,
    double? IncomeChangePercent = null);

/// <param name="PrevSpentCents">Cùng danh mục ở tháng liền trước (0 nếu không có).</param>
/// <param name="SavedCents">Tiết kiệm so với tháng trước = max(0, PrevSpentCents − SpentCents); chỉ có ở tab chi, tab thu luôn 0.</param>
/// <param name="TransactionCount">Số giao dịch của danh mục trong tháng.</param>
/// <param name="BudgetLimitCents">Hạn mức ngân sách THÁNG của danh mục này đúng tháng được hỏi (lấy ảnh chụp của tháng nếu có), null nếu không có ngân sách hoặc tab thu.</param>
/// <param name="BudgetSpentCents">Số đã chi tính cho ngân sách đó (cùng định nghĩa với <paramref name="SpentCents"/>), null khi <paramref name="BudgetLimitCents"/> null. Vượt/còn lại = limit − spent.</param>
public record CategoryBreakdownItemDto(
    Guid? CategoryId,
    string CategoryName,
    string? CategorySlug,
    long SpentCents,
    int Percent,
    long PrevSpentCents = 0,
    long SavedCents = 0,
    int TransactionCount = 0,
    long? BudgetLimitCents = null,
    long? BudgetSpentCents = null);

public record CategoryBreakdownDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalSpentCents,
    IReadOnlyList<CategoryBreakdownItemDto> Items);

public record TimelineDayDto(
    DateOnly Date,
    long TotalSpentCents,
    long TotalIncomeCents,
    IReadOnlyList<TransactionDto> Transactions);

public record TimelineDto(IReadOnlyList<TimelineDayDto> Days, string? NextCursor);

/// <summary>
/// <paramref name="Confidence"/> low/medium/high theo số ngày có dữ liệu — để client không
/// trình bày một con số dựng từ vài ngày như thể nó chắc chắn.
/// </summary>
/// <param name="Advice">
/// Phần "Hành động Đề xuất" của docx Flow 3 mục 4. <c>null</c> khi người dùng chưa khai thu
/// nhập hằng tháng — không có mẫu số thì chỉ nói được "tháng này bạn sẽ tiêu 10,2 triệu",
/// không nói được là âm hay dương, và bịa ra một mức thu nhập để so là tệ hơn im lặng.
/// </param>
public record SpendingForecastDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long SpentSoFarCents,
    long ProjectedSpendCents,
    int DaysElapsed,
    int DaysInMonth,
    int BasedOnDays,
    string Confidence,
    ForecastAdviceDto? Advice = null);

/// <param name="ProjectedBalanceCents">Thu nhập trừ dự báo chi. ÂM nghĩa là bội chi.</param>
/// <param name="SuggestedDailyCutCents">
/// Số tiền cần cắt mỗi ngày cho hết tháng để về hoà. <c>null</c> khi không bội chi.
/// </param>
public record ForecastAdviceDto(
    long MonthlyIncomeCents,
    long ProjectedBalanceCents,
    long? SuggestedDailyCutCents);

public record SpendingInsightDto(
    Guid Id,
    string InsightType,
    string Title,
    string Body,
    Guid? CategoryId,
    string? CategoryName,
    long? AmountCents,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool IsRead,
    DateTimeOffset CreatedAt);
