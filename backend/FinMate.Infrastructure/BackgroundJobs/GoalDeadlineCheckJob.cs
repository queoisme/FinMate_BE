using FinMate.Application.Common.Interfaces;
using FinMate.Application.Groups;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Nhắc user về mục tiêu tiết kiệm đã quá deadline mà chưa hoàn thành. Chỉ nhắc đúng 1 lần
/// (deadline_notified_at) và KHÔNG tự đổi status — đóng hay hủy mục tiêu là quyết định của user.
/// </summary>
public class GoalDeadlineCheckJob
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly IGroupAlertNotifier? _groupAlerts;
    private readonly ISystemConfigProvider? _config;

    public GoalDeadlineCheckJob(
        ISavingGoalRepository savingGoalRepository,
        IUserRepository userRepository,
        IPushNotificationService pushNotificationService,
        IGroupAlertNotifier? groupAlerts = null,
        ISystemConfigProvider? config = null)
    {
        _savingGoalRepository = savingGoalRepository;
        _userRepository = userRepository;
        _pushNotificationService = pushNotificationService;
        _groupAlerts = groupAlerts;
        _config = config;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var goals = await _savingGoalRepository.GetOverdueUnnotifiedGoalsAsync(now, ct);
        var groupsEnabled = _config is not null && (await _config.GetAsync(ct)).Document.Features.Groups;

        foreach (var goal in goals)
        {
            // Đang tắt thì bỏ qua và không đánh dấu, để lượt chạy sau khi bật lại vẫn nhắc.
            if (goal.GroupId is not null && !groupsEnabled)
            {
                continue;
            }

            await NotifyAsync(goal, ct);

            // Đánh dấu kể cả khi user tắt push: đây là cờ chống nhắc lặp, không phải cờ đã gửi.
            goal.DeadlineNotifiedAt = now;
            goal.UpdatedAt = now;
            await _savingGoalRepository.UpdateAsync(goal, ct);
        }
    }

    private async Task NotifyAsync(SavingGoal goal, CancellationToken ct)
    {
        if (goal.UserId is not { } userId)
        {
            if (_groupAlerts is not null)
            {
                await _groupAlerts.SendGoalAlertAsync(goal, "Mục tiêu nhóm quá hạn", $"Mục tiêu \"{goal.Name}\" của nhóm đã qua hạn hoàn thành.", "overdue", ct);
            }

            return;
        }

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is not null && user.NotificationPrefs.PushEnabled)
        {
            await _pushNotificationService.NotifyAsync(
                userId,
                "Mục tiêu tiết kiệm quá hạn",
                $"Mục tiêu \"{goal.Name}\" đã qua hạn hoàn thành. Bạn muốn gia hạn hay hủy?",
                ct: ct);
        }
    }
}
