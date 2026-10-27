namespace FinMate.Application.SavingGoals.Commands;

/// <param name="GoalTemplateCode">Mã mẫu mục tiêu (<c>goal_templates.code</c>) nếu mục tiêu được tạo từ gợi ý; null = người dùng tự đặt.</param>
public record CreateSavingGoalCommand(
    Guid UserId, string Name, long TargetCents, DateTimeOffset? Deadline, string? GoalTemplateCode = null);
