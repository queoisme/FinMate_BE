using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <param name="DedupKey">Cùng <c>(user, kind, dedupKey)</c> chỉ được báo một lần — vd mỗi ngân sách mỗi kỳ, mỗi hóa đơn mỗi lần đến hạn.</param>
/// <param name="HonorQuietHours">Chỉ cảnh báo số dư thấp tôn trọng giờ yên lặng (đúng sơ đồ trang 1).</param>
/// <param name="NotBefore">Không gửi trước lúc này (job đêm phát hiện đổi giá không được push lúc 1 giờ sáng).</param>
public sealed record ForecastAlertRequest(
    Guid UserId,
    ForecastAlertKind Kind,
    string DedupKey,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    bool HonorQuietHours = false,
    DateTimeOffset? NotBefore = null);

public interface IForecastAlertDispatcher
{
    /// <summary>Ghi cảnh báo vào hộp thư rồi gửi ngay hoặc hẹn giờ. Null nếu bị bỏ qua (trùng, user tắt, đang trong khoảng chặn).</summary>
    Task<ForecastAlert?> RaiseAsync(ForecastAlertRequest request, CancellationToken ct = default);

    /// <summary>Gửi các cảnh báo đã hẹn giờ mà nay đã đến giờ. Trả số cảnh báo đã gửi.</summary>
    Task<int> DispatchDueAsync(CancellationToken ct = default);
}

/// <summary>
/// Một cửa cho mọi cảnh báo dự báo: chống lặp, tôn trọng cài đặt của user, hoãn giờ yên lặng. Lưu TRƯỚC rồi mới push — push mà không có bản
/// ghi thì không chống lặp được, còn có bản ghi mà push hỏng thì user vẫn thấy cảnh báo trong app. Payload <c>data</c> mang số tiền: không bao giờ log.
/// </summary>
public class ForecastAlertDispatcher : IForecastAlertDispatcher
{
    public const string ActionForecastAlert = "forecast_alert";
    private const int DispatchBatchSize = 500;

    private readonly IForecastAlertRepository _alerts;
    private readonly IUserRepository _users;
    private readonly IPushNotificationService _push;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public ForecastAlertDispatcher(IForecastAlertRepository alerts, IUserRepository users, IPushNotificationService push, ISystemConfigProvider config)
        : this(alerts, users, push, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal ForecastAlertDispatcher(
        IForecastAlertRepository alerts, IUserRepository users, IPushNotificationService push, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _alerts = alerts;
        _users = users;
        _push = push;
        _config = config;
        _now = now;
    }

    public async Task<ForecastAlert?> RaiseAsync(ForecastAlertRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null || !Wants(user, request.Kind) || await _alerts.ExistsAsync(request.UserId, request.Kind, request.DedupKey, ct))
        {
            return null;
        }

        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var now = _now();
        if (request.Kind == ForecastAlertKind.LowBalance && await IsLowBalanceBlockedAsync(request.UserId, settings, now, ct))
        {
            return null;
        }

        var scheduledFor = ScheduleFor(request, user, settings, now);
        var alert = new ForecastAlert
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Kind = request.Kind,
            DedupKey = request.DedupKey,
            Title = request.Title,
            Body = request.Body,
            Status = ForecastAlertStatus.Pending,
            ScheduledFor = scheduledFor,
            CreatedAt = now,
        };
        alert.Data = JsonSerializer.Serialize(Payload(alert, request.Data));

        var sendNow = scheduledFor <= now;
        if (sendNow)
        {
            alert.Status = ForecastAlertStatus.Sent;
            alert.SentAt = now;
        }

        if (!await _alerts.TryAddAsync(alert, ct))
        {
            return null;
        }

        if (sendNow)
        {
            await _push.NotifyAsync(alert.UserId, alert.Title, alert.Body, Payload(alert, request.Data), ct);
        }

        return alert;
    }

    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        var now = _now();
        var due = await _alerts.ListDuePendingAsync(now, DispatchBatchSize, ct);
        foreach (var alert in due)
        {
            alert.Status = ForecastAlertStatus.Sent;
            alert.SentAt = now;
            await _alerts.UpdateAsync(alert, ct);

            var user = await _users.GetByIdAsync(alert.UserId, ct);
            if (user is not null && Wants(user, alert.Kind))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(alert.Data) ?? [];
                await _push.NotifyAsync(alert.UserId, alert.Title, alert.Body, data, ct);
            }
        }

        return due.Count;
    }

    /// <summary>Cảnh báo vượt ngân sách sớm theo công tắc cảnh báo ngân sách; sức khoẻ listener theo công tắc riêng; mọi loại còn lại theo công tắc cảnh báo dự báo.</summary>
    private static bool Wants(User user, ForecastAlertKind kind) => kind switch
    {
        ForecastAlertKind.BudgetRisk or ForecastAlertKind.GroupBudget => user.NotificationPrefs.BudgetAlertsEnabled,
        ForecastAlertKind.GroupGoal => true,   // nhắc mục tiêu cá nhân cũng chỉ theo công tắc push; IPushNotificationService tự chặn
        ForecastAlertKind.ListenerHealth => user.NotificationPrefs.ListenerHealthAlertsEnabled,   // D18: không phụ thuộc công tắc dự báo
        _ => user.NotificationPrefs.ForecastAlertsEnabled,
    };

    /// <summary>"Đã gửi cảnh báo tương tự gần đây?" và phản hồi "không chính xác" gần đây.</summary>
    private async Task<bool> IsLowBalanceBlockedAsync(Guid userId, Admin.SystemConfig.ForecastSettings settings, DateTimeOffset now, CancellationToken ct)
        => await _alerts.AnyCreatedSinceAsync(userId, ForecastAlertKind.LowBalance, now.AddDays(-settings.LowBalanceCooldownDays), ct)
           || await _alerts.AnyMarkedInaccurateSinceAsync(userId, ForecastAlertKind.LowBalance, now.AddDays(-settings.InaccurateSuppressDays), ct);

    private static DateTimeOffset ScheduleFor(ForecastAlertRequest request, User user, Admin.SystemConfig.ForecastSettings settings, DateTimeOffset now)
    {
        var at = request.NotBefore is { } notBefore && notBefore > now ? notBefore : now;
        if (!request.HonorQuietHours)
        {
            return at;
        }

        var quiet = QuietHoursOf(user, settings);
        return quiet.Contains(at) ? quiet.EndAfter(at) : at;
    }

    internal static QuietHours QuietHoursOf(User user, Admin.SystemConfig.ForecastSettings settings)
        => user.QuietHoursStartMinute is { } start && user.QuietHoursEndMinute is { } end
            ? new QuietHours(start, end)
            : new QuietHours(settings.QuietHoursStartMinute, settings.QuietHoursEndMinute);

    private static Dictionary<string, string> Payload(ForecastAlert alert, IReadOnlyDictionary<string, string> extra)
    {
        var data = new Dictionary<string, string>(extra)
        {
            ["action"] = ActionForecastAlert,
            ["kind"] = ForecastAlertKinds.Text(alert.Kind),
            ["alertId"] = alert.Id.ToString(),
        };
        return data;
    }
}

public static class ForecastAlertKinds
{
    public static string Text(ForecastAlertKind kind) => kind switch
    {
        ForecastAlertKind.LowBalance => "low_balance",
        ForecastAlertKind.BudgetRisk => "budget_risk",
        ForecastAlertKind.BillDue => "bill_due",
        ForecastAlertKind.BillStatusCheck => "bill_status_check",
        ForecastAlertKind.IncomeLate => "income_late",
        ForecastAlertKind.PriceChange => "price_change",
        ForecastAlertKind.Anomaly => "anomaly",
        ForecastAlertKind.GroupBudget => "group_budget",
        ForecastAlertKind.GroupGoal => "group_goal",
        ForecastAlertKind.MarketChange => "market_change",
        ForecastAlertKind.ListenerHealth => "listener_health",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>08:00 giờ VN kế tiếp tính từ <paramref name="now"/> (hoặc hôm nay nếu chưa tới): cảnh báo do job đêm tạo ra chờ tới sáng mới gửi.</summary>
    public static DateTimeOffset NextMorning(DateTimeOffset now)
    {
        const int morningHour = 8;
        var local = VietnamTime.ToLocal(now);
        var today = new DateTimeOffset(local.Date, VietnamTime.Offset).AddHours(morningHour);
        return today > local ? today : today.AddDays(1);
    }
}
