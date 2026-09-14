using FinMate.Application.Common.Interfaces;

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

    public GoalDeadlineCheckJob(
        ISavingGoalRepository savingGoalRepository,
        IUserRepository userRepository,
        IPushNotificationService pushNotificationService)
    {
        _savingGoalRepository = savingGoalRepository;
        _userRepository = userRepository;
        _pushNotificationService = pushNotificationService;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var goals = await _savingGoalRepository.GetOverdueUnnotifiedGoalsAsync(now, ct);

        foreach (var goal in goals)
        {
            var user = await _userRepository.GetByIdAsync(goal.UserId, ct);
            if (user is not null && user.NotificationPrefs.PushEnabled)
            {
                await _pushNotificationService.NotifyAsync(
                    goal.UserId,
                    "Mục tiêu tiết kiệm quá hạn",
                    $"Mục tiêu \"{goal.Name}\" đã qua hạn hoàn thành. Bạn muốn gia hạn hay hủy?",
                    ct: ct);
            }

            // Đánh dấu kể cả khi user tắt push: đây là cờ chống nhắc lặp, không phải cờ đã gửi.
            goal.DeadlineNotifiedAt = now;
            goal.UpdatedAt = now;
            await _savingGoalRepository.UpdateAsync(goal, ct);
        }
    }
}
