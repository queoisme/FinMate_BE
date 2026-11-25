using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public static class PurchaseReminderErrorCodes
{
    public const string InvalidDate = "PURCHASE_REMINDER_INVALID_DATE";
}

public sealed record PurchaseReminderDto(DateOnly Date, DateTimeOffset RemindAt, bool Created);

public interface IPurchaseReminderService
{
    /// <summary>
    /// Hẹn một lần nhắc "hôm nay có thể xem lại khoản định mua" vào 08:00 (giờ Việt Nam) của <paramref name="date"/>. Ngày phải sau hôm nay và không quá <see cref="PurchaseReminderService.MaxDaysAhead"/> ngày.
    /// Hẹn lại cùng một ngày thì không tạo thêm (<c>Created = false</c>).
    /// </summary>
    Task<PurchaseReminderDto> ScheduleAsync(Guid userId, DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// Nhắc ngày mua đi qua hộp thư <c>forecast_alerts</c>: lưu trước, hẹn giờ bằng <c>ScheduledFor</c>, job gửi cảnh báo đến hạn sẵn có sẽ push. Nội dung cố định, không mang số tiền hay tên món
/// (người dùng không phải nhập gì thêm, và push không lộ số tiền trên màn hình khóa).
/// </summary>
public sealed class PurchaseReminderService(IForecastAlertDispatcher dispatcher) : IPurchaseReminderService
{
    public const int MaxDaysAhead = 365;
    private const int MorningHour = 8;

    public async Task<PurchaseReminderDto> ScheduleAsync(Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var today = VietnamTime.Today();
        if (date <= today || date > today.AddDays(MaxDaysAhead))
        {
            throw new BusinessRuleException(PurchaseReminderErrorCodes.InvalidDate, $"Ngày nhắc phải sau hôm nay và trong vòng {MaxDaysAhead} ngày tới.");
        }

        var remindAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), VietnamTime.Offset).AddHours(MorningHour);
        var alert = await dispatcher.RaiseAsync(
            new ForecastAlertRequest(
                userId,
                ForecastAlertKind.PurchaseReminder,
                $"purchase_reminder:{date:yyyy-MM-dd}",
                "Đến ngày bạn hẹn xem lại khoản mua",
                "Hôm nay bạn đã hẹn xem lại khoản định mua. Mở FinMate để kiểm tra lại số dư và kế hoạch.",
                new Dictionary<string, string> { ["date"] = date.ToString("yyyy-MM-dd") },
                NotBefore: remindAt),
            ct);

        return new PurchaseReminderDto(date, remindAt, alert is not null);
    }
}
