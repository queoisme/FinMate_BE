using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Cảnh báo khi chi tiêu của chu kỳ chạm 80% / 100% hạn mức (ARCHITECTURE.md §5, mỗi giờ).
/// Mỗi ngưỡng gửi đúng 1 lần cho mỗi chu kỳ — dấu vết là alert_80_sent_at / alert_100_sent_at.
/// </summary>
public class BudgetAlertJob
{
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
            var sent = false;

            if (period.SpentCents >= period.LimitCents && period.Alert100SentAt is null)
            {
                await _pushNotificationService.NotifyAsync(
                    budget.UserId,
                    "Vượt hạn mức chi tiêu",
                    $"Bạn đã vượt hạn mức {scope} trong chu kỳ này.",
                    ct);
                period.Alert100SentAt = now;

                // Chi tiêu có thể nhảy thẳng từ dưới 80% lên quá 100% giữa 2 lần chạy job.
                // Đóng luôn ngưỡng 80% để lần chạy sau không gửi ngược cảnh báo nhẹ hơn.
                period.Alert80SentAt ??= now;
                sent = true;
            }
            else if (period.SpentCents * 100 >= period.LimitCents * 80 && period.Alert80SentAt is null)
            {
                await _pushNotificationService.NotifyAsync(
                    budget.UserId,
                    "Sắp chạm hạn mức chi tiêu",
                    $"Bạn đã dùng hơn 80% hạn mức {scope} trong chu kỳ này.",
                    ct);
                period.Alert80SentAt = now;
                sent = true;
            }

            if (sent)
            {
                period.UpdatedAt = now;
                await _budgetRepository.UpdatePeriodAsync(period, ct);
            }
        }
    }
}
