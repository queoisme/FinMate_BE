using FinMate.Application.Common.Models;

namespace FinMate.Application.SavingGoals;

/// <summary>
/// Công thức khả thi của một mục tiêu (docx Flow 3 bước 2.2), tách ra để DÙNG CHUNG giữa tiến độ của mục tiêu đã có
/// (<c>GetGoalProgressQueryHandler</c>) và bản xem trước cho mục tiêu chưa tạo (<c>GoalPlanningService</c>) — hai nơi cùng một công thức
/// thì mới không lệch nhau khi ai đó chỉnh một bên.
/// </summary>
internal static class GoalFeasibilityCalculator
{
    /// <summary>Làm tròn LÊN: còn 45 ngày là còn 2 tháng để góp, không phải 1,5 tháng.</summary>
    public static int MonthsRemaining(DateTimeOffset deadline, DateTimeOffset now)
        => Math.Max(1, (int)Math.Ceiling((deadline - now).TotalDays / 30.0));

    public static long RequiredPerMonth(long remainingCents, DateTimeOffset deadline, DateTimeOffset now)
        => (long)Math.Ceiling(remainingCents / (double)MonthsRemaining(deadline, now));

    /// <param name="remainingCents">Phần còn thiếu, &gt; 0.</param>
    /// <param name="incomeCents">Thu nhập dự kiến; null hoặc ≤ 0 = chưa khai.</param>
    /// <param name="budgetLimitsCents">Tổng hạn mức ngân sách đã đặt.</param>
    public static GoalFeasibilityDto Compute(
        long remainingCents,
        long savedCents,
        DateTimeOffset deadline,
        DateTimeOffset now,
        long? incomeCents,
        long budgetLimitsCents)
    {
        var monthsRemaining = MonthsRemaining(deadline, now);
        var requiredPerMonth = RequiredPerMonth(remainingCents, deadline, now);

        if (incomeCents is not { } income || income <= 0)
        {
            // Chưa khai thu nhập: vẫn trả về "mỗi tháng cần bao nhiêu" vì đó là con số hữu ích
            // tự thân, nhưng không kết luận khả thi hay không.
            return new GoalFeasibilityDto(requiredPerMonth, null, null, null, null);
        }

        var available = income - budgetLimitsCents;
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
            var monthsNeeded = (int)Math.Ceiling(remainingCents / (double)available);
            suggestedDeadline = now.AddMonths(monthsNeeded);
            suggestedTarget = savedCents + available * monthsRemaining;
        }

        return new GoalFeasibilityDto(requiredPerMonth, available, false, suggestedDeadline, suggestedTarget);
    }
}
