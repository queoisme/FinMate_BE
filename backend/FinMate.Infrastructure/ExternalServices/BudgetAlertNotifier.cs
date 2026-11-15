using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.ExternalServices;

public class BudgetAlertNotifier : IBudgetAlertNotifier
{
    private readonly IUserRepository _userRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IUserNotificationService _userNotifications;

    // Scoped cùng DbContext nên sống đúng một request hoặc một lượt chạy job.
    private readonly Dictionary<Guid, bool> _enabled = [];

    public BudgetAlertNotifier(
        IUserRepository userRepository, IPushNotificationService pushNotificationService, IUserNotificationService userNotifications)
    {
        _userRepository = userRepository;
        _pushNotificationService = pushNotificationService;
        _userNotifications = userNotifications;
    }

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default)
    {
        if (_enabled.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var user = await _userRepository.GetByIdAsync(userId, ct);
        var enabled = user is not null
            && user.NotificationPrefs.PushEnabled
            && user.NotificationPrefs.BudgetAlertsEnabled;

        _enabled[userId] = enabled;
        return enabled;
    }

    public async Task SendAsync(IReadOnlyList<BudgetAlert> alerts, CancellationToken ct = default)
    {
        foreach (var alert in alerts)
        {
            // Caller đã hỏi IsEnabledAsync trước khi đánh dấu, nhưng hỏi lại ở đây thì rẻ
            // (đã cache) và không endpoint nào mới thêm sau này có thể vô tình bỏ qua nó.
            await _userNotifications.CreateAsync(alert.UserId, UserNotificationType.BudgetThreshold, alert.Title, alert.Body, null, null, ct);

            if (await IsEnabledAsync(alert.UserId, ct))
            {
                await _pushNotificationService.NotifyAsync(alert.UserId, alert.Title, alert.Body, ct: ct);
            }
        }
    }
}
