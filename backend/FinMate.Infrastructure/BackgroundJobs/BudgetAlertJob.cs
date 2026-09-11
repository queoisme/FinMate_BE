using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Cảnh báo khi chi tiêu của chu kỳ chạm 70% / 90% / 100% hạn mức (ARCHITECTURE.md §5, mỗi giờ).
/// 3 mốc theo Core Flow 3 — xem ARCHITECTURE.md §0 quyết định #2.
/// Mỗi ngưỡng gửi đúng 1 lần cho mỗi chu kỳ — dấu vết là alert_70/90/100_sent_at.
/// </summary>
public class BudgetAlertJob
{
    /// <summary>
    /// Các mốc theo thứ tự GIẢM DẦN — bắt buộc, vì thuật toán bên dưới lấy mốc cao nhất đã
    /// chạm rồi đóng mọi mốc thấp hơn.
    /// </summary>
    private static readonly BudgetAlertThreshold[] Thresholds =
    [
        new(100, "Vượt hạn mức chi tiêu", scope => $"Bạn đã vượt hạn mức {scope} trong chu kỳ này.",
            p => p.Alert100SentAt, (p, at) => p.Alert100SentAt = at),
        new(90, "Sắp cạn hạn mức chi tiêu", scope => $"Bạn đã dùng hơn 90% hạn mức {scope} trong chu kỳ này.",
            p => p.Alert90SentAt, (p, at) => p.Alert90SentAt = at),
        new(70, "Đã dùng quá 70% hạn mức", scope => $"Bạn đã dùng hơn 70% hạn mức {scope} trong chu kỳ này.",
            p => p.Alert70SentAt, (p, at) => p.Alert70SentAt = at),
    ];

    private readonly IBudgetRepository _budgetRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPushNotificationService _pushNotificationService;

    public BudgetAlertJob(
        IBudgetRepository budgetRepository,
        IUserRepository userRepository,
        IPushNotificationService pushNotificationService)
    {
        _budgetRepository = budgetRepository;
        _userRepository = userRepository;
        _pushNotificationService = pushNotificationService;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _budgetRepository.GetPeriodsForAlertAsync(now, ct);

        // Nhiều budget có thể thuộc cùng 1 user — cache trong lượt chạy để không query lặp.
        var users = new Dictionary<Guid, User?>();

        foreach (var (period, budget) in candidates)
        {
            if (!users.TryGetValue(budget.UserId, out var user))
            {
                user = await _userRepository.GetByIdAsync(budget.UserId, ct);
                users[budget.UserId] = user;
            }

            if (user is null || !user.NotificationPrefs.PushEnabled || !user.NotificationPrefs.BudgetAlertsEnabled)
            {
                continue;
            }

            var scope = budget.Category?.Name ?? "toàn bộ chi tiêu";

            // Chỉ gửi mốc CAO NHẤT đã chạm mà chưa từng gửi. Chi tiêu có thể nhảy vọt qua
            // nhiều mốc giữa 2 lần chạy job (mua 1 món hết nửa hạn mức) — gửi cả 3 thông báo
            // cùng lúc là spam, và gửi mốc thấp sau khi user đã vượt 100% thì sai hẳn thông
            // điệp. Các mốc thấp hơn được đánh dấu đã gửi để lần sau không bắn ngược trở lại.
            var reached = Array.FindIndex(Thresholds, t => period.SpentCents * 100 >= period.LimitCents * t.Percent);
            if (reached < 0)
            {
                continue;
            }

            var highest = Thresholds[reached];
            if (highest.GetSentAt(period) is not null)
            {
                continue;
            }

            await _pushNotificationService.NotifyAsync(budget.UserId, highest.Title, highest.Body(scope), ct);

            foreach (var threshold in Thresholds[reached..])
            {
                if (threshold.GetSentAt(period) is null)
                {
                    threshold.SetSentAt(period, now);
                }
            }

            period.UpdatedAt = now;
            await _budgetRepository.UpdatePeriodAsync(period, ct);
        }
    }

    private sealed record BudgetAlertThreshold(
        int Percent,
        string Title,
        Func<string, string> Body,
        Func<BudgetPeriod, DateTimeOffset?> GetSentAt,
        Action<BudgetPeriod, DateTimeOffset> SetSentAt);
}
