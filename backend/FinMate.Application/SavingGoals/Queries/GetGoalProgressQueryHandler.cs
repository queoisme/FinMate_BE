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
    private readonly IUserRepository _userRepository;
    private readonly IBudgetRepository _budgetRepository;

    public GetGoalProgressQueryHandler(
        ISavingGoalRepository savingGoalRepository,
        IUserRepository userRepository,
        IBudgetRepository budgetRepository)
    {
        _savingGoalRepository = savingGoalRepository;
        _userRepository = userRepository;
        _budgetRepository = budgetRepository;
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
            contributions.Select(SavingGoalMapper.ToDto).ToList(),
            await BuildFeasibilityAsync(goal, remaining, now, query.UserId, ct));
    }

    /// <summary>
    /// Docx Flow 3 bước 2.2: chia đều phần còn thiếu theo tháng rồi đối chiếu với phần thu
    /// nhập còn dư sau khi trừ tổng hạn mức ngân sách.
    ///
    /// Khác <see cref="IsOnTrack"/>: cái kia hỏi "đang đi đúng nhịp chưa" dựa trên tiến độ đã
    /// có, cái này hỏi "nhịp đó có nằm trong khả năng tài chính không" — một mục tiêu mới tinh
    /// luôn đúng nhịp nhưng vẫn có thể bất khả thi ngay từ đầu.
    /// </summary>
    private async Task<GoalFeasibilityDto?> BuildFeasibilityAsync(
        SavingGoal goal, long remaining, DateTimeOffset now, Guid userId, CancellationToken ct)
    {
        if (goal.Deadline is null || remaining <= 0 || goal.Status != SavingGoalStatus.Active)
        {
            return null;
        }

        // Làm tròn LÊN: còn 45 ngày là còn 2 tháng để góp, không phải 1,5 tháng.
        var monthsRemaining = Math.Max(1, (int)Math.Ceiling((goal.Deadline.Value - now).TotalDays / 30.0));
        var requiredPerMonth = (long)Math.Ceiling(remaining / (double)monthsRemaining);

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user?.MonthlyIncomeCents is not { } income || income <= 0)
        {
            // Chưa khai thu nhập: vẫn trả về "mỗi tháng cần bao nhiêu" vì đó là con số hữu ích
            // tự thân, nhưng không kết luận khả thi hay không.
            return new GoalFeasibilityDto(requiredPerMonth, null, null, null, null);
        }

        var budgets = await _budgetRepository.GetListForUserAsync(userId, ct);
        var available = income - budgets.Sum(b => b.LimitCents);

        if (requiredPerMonth <= available)
        {
            return new GoalFeasibilityDto(requiredPerMonth, available, true, null, null);
        }

        // Hai hướng điều chỉnh docx nêu. Chỉ gợi ý kéo dài hạn khi còn dư ra được đồng nào —
        // thu nhập đã hết sạch vào ngân sách thì kéo dài bao lâu cũng không góp nổi.
        DateTimeOffset? suggestedDeadline = null;
        long? suggestedTarget = null;

        if (available > 0)
        {
            var monthsNeeded = (int)Math.Ceiling(remaining / (double)available);
            suggestedDeadline = now.AddMonths(monthsNeeded);
            suggestedTarget = goal.SavedCents + available * monthsRemaining;
        }

        return new GoalFeasibilityDto(requiredPerMonth, available, false, suggestedDeadline, suggestedTarget);
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
