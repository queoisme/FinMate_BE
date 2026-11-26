using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Một giao dịch đã xác nhận dùng làm dữ liệu dự báo. Không gồm chuyển khoản nội bộ. Dữ liệu thói quen không gồm cho vay/mượn (không phải
/// thu/chi thật); dữ liệu thô để dựng lại số dư thì có, đánh dấu bằng <paramref name="IsLoan"/> (Phase 86, D23).
/// <paramref name="CounterpartyKey"/> đã chuẩn hóa từ tên người nhận/cửa hàng.
/// </summary>
public sealed record ForecastTransaction(
    Guid Id,
    Guid AccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType Type,
    DateTimeOffset TransactedAt,
    DateOnly Date,
    string? MerchantName,
    string? CounterpartyKey,
    bool ExcludedFromForecast,
    TransactionRelation? Relation = null,
    Guid? RelatedTransactionId = null,
    bool IsReversed = false,
    Guid? DuplicateOfTransactionId = null,  // Phase 82: liên kết trùng (nghi trùng hoặc đã bác), để phát hiện bất thường không gắn cờ lại cặp này
    bool IsLoan = false);

/// <summary>Bản rút gọn của khoản định kỳ mà các bộ tính toán cần (không kéo entity EF vào hàm thuần).</summary>
/// <param name="RemainingOccurrences">
/// Phase 92: số kỳ còn phải trả của khoản có <c>total_occurrences</c> (tổng − đã trả, không âm); null = không có kỳ cuối theo số kỳ.
/// Lần thứ i tính từ <see cref="NextDueDate"/> là kỳ "đã trả + i".
/// </param>
/// <param name="EndDate">Phase 92: không chiếu kỳ nào sau ngày này; null = không có.</param>
public sealed record RecurringItemView(
    Guid Id,
    RecurringDirection Direction,
    string Name,
    string? CounterpartyKey,
    Guid? AccountId,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    int? PreferredDay,
    int? RemainingOccurrences = null,
    DateOnly? EndDate = null)
{
    /// <summary>Không còn kỳ nào để chiếu: đã trả đủ số kỳ, hoặc kỳ kế tiếp đã qua ngày kết thúc.</summary>
    public bool Exhausted => RemainingOccurrences is <= 0 || (EndDate is { } end && NextDueDate > end);

    /// <summary>S1: khoản trả góp theo tổng số kỳ — kỳ chi quá hạn không tiêu kỳ, không bị tạm dừng.</summary>
    public bool CountsOccurrences => RemainingOccurrences is not null;
}

/// <summary>Các ngưỡng mà nhiều bộ tính toán dùng chung, lấy từ cấu hình hệ thống <c>forecast</c>.</summary>
public sealed record ForecastRules(
    int HistoryWindowDays,
    int MinHistoryDays,
    int AmountTolerancePercent,
    int MonthlyMinIntervalDays,
    int MonthlyMaxIntervalDays,
    int WeeklyMinIntervalDays,
    int WeeklyMaxIntervalDays,
    int SuggestMinOccurrences,
    int AutoActivateOccurrences,
    int OverdueGraceDays)
{
    public static ForecastRules From(Admin.SystemConfig.ForecastSettings s) => new(
        s.HistoryWindowDays, s.MinHistoryDays, s.AmountTolerancePercent,
        s.MonthlyMinIntervalDays, s.MonthlyMaxIntervalDays, s.WeeklyMinIntervalDays, s.WeeklyMaxIntervalDays,
        s.SuggestMinOccurrences, s.AutoActivateOccurrences, s.OverdueGraceDays);

    public bool AmountWithinTolerance(long reference, long amount)
        => reference > 0 && Math.Abs(amount - reference) * 100 <= reference * AmountTolerancePercent;
}
