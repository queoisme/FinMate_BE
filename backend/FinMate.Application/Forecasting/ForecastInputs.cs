using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Một giao dịch đã xác nhận dùng làm dữ liệu dự báo. Không gồm chuyển khoản nội bộ và cho vay/mượn (không phải thu/chi thật).
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
    bool IsReversed = false);

/// <summary>Bản rút gọn của khoản định kỳ mà các bộ tính toán cần (không kéo entity EF vào hàm thuần).</summary>
public sealed record RecurringItemView(
    Guid Id,
    RecurringDirection Direction,
    string Name,
    string? CounterpartyKey,
    Guid? AccountId,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    int? PreferredDay);

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
