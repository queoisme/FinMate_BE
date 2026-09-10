using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.SavingGoals.Queries;

public class GetGoalProgressQueryHandler : IGetGoalProgressQueryHandler
{
    private const int RecentContributionsLimit = 10;

    private readonly ISavingGoalRepository _savingGoalRepository;

    public GetGoalProgressQueryHandler(ISavingGoalRepository savingGoalRepository)
    {
        _savingGoalRepository = savingGoalRepository;
    }

    public async Task<GoalProgressDto> HandleAsync(GetGoalProgressQuery query, CancellationToken ct = default)
    {
        var goal = await _savingGoalRepository.GetByIdAsync(query.GoalId, query.UserId, ct)
            ?? throw new NotFoundException("SavingGoal", query.GoalId);

        var contributions = await _savingGoalRepository.GetContributionsAsync(
            goal.Id, query.UserId, RecentContributionsLimit, ct);

        var now = DateTimeOffset.UtcNow;
        var remaining = Math.Max(0, goal.TargetCents - goal.SavedCents);

        int? daysRemaining = null;
        long? requiredPerDay = null;

        if (goal.Deadline is not null)
        {
            daysRemaining = (int)Math.Ceiling((goal.Deadline.Value - now).TotalDays);
            if (remaining > 0)
            {
                // Quá hạn hoặc còn dưới 1 ngày → dồn hết phần thiếu vào 1 ngày.
                var days = Math.Max(1, daysRemaining.Value);
                requiredPerDay = (long)Math.Ceiling(remaining / (double)days);
            }
        }

        return new GoalProgressDto(
            goal.Id,
            goal.Name,
            goal.Status.ToString().ToLowerInvariant(),
            goal.TargetCents,
            goal.SavedCents,
            remaining,
            SavingGoalMapper.PercentComplete(goal),
            goal.Deadline,
            daysRemaining,
            requiredPerDay,
            IsOnTrack(goal, now),
            contributions.Select(SavingGoalMapper.ToDto).ToList());
    }

    /// <summary>
    /// So tiến độ thực tế với tiến độ tuyến tính kỳ vọng từ lúc tạo tới deadline. Mục tiêu
    /// không đặt deadline thì không có nhịp bắt buộc nào để lệch — luôn coi là đúng tiến độ.
    /// </summary>
    private static bool IsOnTrack(SavingGoal goal, DateTimeOffset now)
    {
        if (goal.Status == SavingGoalStatus.Completed)
        {
            return true;
        }

        if (goal.Deadline is null)
        {
            return true;
        }

        if (now >= goal.Deadline.Value)
        {
            return goal.SavedCents >= goal.TargetCents;
        }

        var totalDays = (goal.Deadline.Value - goal.CreatedAt).TotalDays;
        if (totalDays <= 0)
        {
            return goal.SavedCents >= goal.TargetCents;
        }

        var elapsedDays = Math.Max(0, (now - goal.CreatedAt).TotalDays);
        var expected = goal.TargetCents * (elapsedDays / totalDays);

        // Mốc kỳ vọng trôi liên tục theo thời gian, nên so bằng "≥" đúng nghĩa đen sẽ lật
        // trạng thái vì vài mili-giây: user tiết kiệm đúng 50% ở đúng nửa chặng vẫn bị coi
        // là trễ. Cho biên 1% mục tiêu để "đang bám đúng nhịp" không thành nhiễu.
        var tolerance = goal.TargetCents / 100.0;

        return goal.SavedCents + tolerance >= expected;
    }
}
