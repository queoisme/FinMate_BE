namespace FinMate.Application.Common.Models;

/// <summary>
/// Phần thưởng vừa nhận được kèm theo một lần đóng góp — để mascot ăn mừng ngay mà client
/// không phải gọi thêm một vòng tới /gamification.
/// </summary>
public record GoalCelebrationDto(
    bool LeveledUp,
    int Level,
    IReadOnlyList<string> UnlockedItemNames);

public record SavingGoalDto(
    Guid Id,
    string Name,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    string Status,
    DateTimeOffset? Deadline,
    DateTimeOffset? CompletedAt,
    GoalCelebrationDto? Celebration = null);

public record GoalContributionDto(
    Guid Id,
    long AmountCents,
    string? Note,
    DateTimeOffset ContributedAt);

public record GoalProgressDto(
    Guid GoalId,
    string Name,
    string Status,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    DateTimeOffset? Deadline,
    int? DaysRemaining,
    long? RequiredPerDayCents,
    bool IsOnTrack,
    IReadOnlyList<GoalContributionDto> RecentContributions,
    GoalFeasibilityDto? Feasibility = null);

/// <summary>
/// Kiểm tra tính khả thi của mục tiêu (docx Flow 3 bước 2.2): chia đều số còn thiếu theo
/// tháng rồi đối chiếu với thu nhập trừ tổng hạn mức ngân sách.
///
/// Đây là CẢNH BÁO KÈM GỢI Ý, không phải từ chối — docx nói "gợi ý điều chỉnh kéo dài thời
/// hạn hoặc giảm bớt số tiền mục tiêu". Người dùng vẫn được đặt mục tiêu tham vọng.
///
/// <c>null</c> khi mục tiêu không có hạn chót: không có mốc thời gian thì không có "mỗi
/// tháng cần bao nhiêu" để mà đối chiếu.
/// </summary>
/// <param name="AvailablePerMonthCents">
/// Thu nhập trừ tổng hạn mức ngân sách đã đặt. <c>null</c> khi người dùng chưa khai thu nhập.
/// </param>
/// <param name="IsFeasible"><c>null</c> khi chưa đủ dữ liệu để kết luận.</param>
public record GoalFeasibilityDto(
    long RequiredPerMonthCents,
    long? AvailablePerMonthCents,
    bool? IsFeasible,
    DateTimeOffset? SuggestedDeadline,
    long? SuggestedTargetCents);
