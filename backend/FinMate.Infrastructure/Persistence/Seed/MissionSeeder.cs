using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Bộ mission mặc định. Idempotent theo <c>code</c> như CategorySeeder/ProviderConfigSeeder —
/// chạy lại mỗi lần khởi động chỉ thêm mission mới, không đụng mission đã có (Phase 8 sẽ cho
/// admin sửa nội dung, seeder không được ghi đè lên chỉnh sửa đó).
/// </summary>
public static class MissionSeeder
{
    private static readonly (string Code, string Title, string Description, MissionPeriodType Period, MissionConditionType Condition, int Target, int Exp)[] Missions =
    {
        ("daily_confirm_3", "Xác nhận 3 giao dịch", "Xác nhận 3 giao dịch trong hôm nay.",
            MissionPeriodType.Daily, MissionConditionType.ConfirmTransaction, 3, 50),
        ("daily_manual_1", "Tự thêm 1 giao dịch", "Tự nhập 1 giao dịch trong hôm nay.",
            MissionPeriodType.Daily, MissionConditionType.CreateManualTransaction, 1, 30),
        ("weekly_confirm_15", "Xác nhận 15 giao dịch", "Xác nhận 15 giao dịch trong tuần này.",
            MissionPeriodType.Weekly, MissionConditionType.ConfirmTransaction, 15, 150),
        ("weekly_contribute_1", "Bỏ ống heo", "Đóng góp vào một mục tiêu tiết kiệm trong tuần này.",
            MissionPeriodType.Weekly, MissionConditionType.ContributeToGoal, 1, 200),
        ("first_transaction", "Giao dịch đầu tiên", "Ghi nhận giao dịch đầu tiên của bạn.",
            MissionPeriodType.OneTime, MissionConditionType.ConfirmTransaction, 1, 100),
        ("first_goal_contribution", "Khởi động mục tiêu", "Đóng góp lần đầu vào một mục tiêu tiết kiệm.",
            MissionPeriodType.OneTime, MissionConditionType.ContributeToGoal, 1, 100),
        ("streak_7", "Chuỗi 7 ngày", "Duy trì chuỗi hoạt động 7 ngày liên tiếp.",
            MissionPeriodType.OneTime, MissionConditionType.LoginStreak, 7, 200),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existing = await context.Missions.Select(m => m.Code).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = false;

        foreach (var (code, title, description, period, condition, target, exp) in Missions)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            context.Missions.Add(new Mission
            {
                Id = Guid.NewGuid(),
                Code = code,
                Title = title,
                Description = description,
                PeriodType = period,
                ConditionType = condition,
                ConditionTarget = target,
                ExpReward = exp,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
