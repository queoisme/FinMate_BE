namespace FinMate.Application.Common.Models;

/// <summary>
/// Dòng tiền của một kỳ (Phase 90, Q16, D1–D3). <c>totalSpentCents</c> / <c>totalIncomeCents</c> là chi tiêu / thu nhập THẬT; tiền đổi chỗ nằm ở đây.
/// Bất biến: <c>NetCents = thu − chi − LoanOutflow + LoanInflow − GroupFundOutflow + GroupFundInflow − TransferOutflow + TransferInflow
/// + RefundSurplus + ReversalNet</c>.
/// </summary>
/// <param name="NetCents">Tổng tác động số dư của các giao dịch ĐÃ XÁC NHẬN trong kỳ (không gồm số dư ban đầu của ví tạo giữa kỳ, không gồm nháp).</param>
/// <param name="LoanOutflowCents">Giao dịch danh mục <c>loan</c> làm tiền ra (cho vay, trả nợ).</param>
/// <param name="LoanInflowCents">Giao dịch danh mục <c>loan</c> làm tiền vào (đi vay, được trả).</param>
/// <param name="GroupFundOutflowCents">Góp quỹ nhóm, chi quỹ trả bằng ví (Phase 85).</param>
/// <param name="GroupFundInflowCents">Rút từ quỹ nhóm về ví.</param>
/// <param name="TransferOutflowCents">Chuyển sang ví khác của chính mình; chỉ khác 0 khi xem một ví. Phí chuyển ví là khoản chi riêng.</param>
/// <param name="TransferInflowCents">Nhận từ ví khác của chính mình; chỉ khác 0 khi xem một ví.</param>
/// <param name="RefundSurplusCents">max(0, tổng hoàn − tổng chi gộp) của kỳ: phần chi ròng âm bị chặn để <c>totalSpentCents</c> không âm.</param>
/// <param name="ReversalNetCents">Tác động số dư của cặp đảo giao dịch (có dấu): âm khi khoản gốc bị đảo ở kỳ này mà lệnh đảo ở kỳ khác, dương khi ngược lại, 0 khi cả cặp cùng kỳ.</param>
public record CashFlowDto(
    long NetCents,
    long LoanOutflowCents,
    long LoanInflowCents,
    long GroupFundOutflowCents,
    long GroupFundInflowCents,
    long TransferOutflowCents,
    long TransferInflowCents,
    long RefundSurplusCents,
    long ReversalNetCents);

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
    double? IncomeChangePercent = null,
    CashFlowDto? CashFlow = null);

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
    IReadOnlyList<TransactionDto> Transactions,
    CashFlowDto? CashFlow = null);

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
