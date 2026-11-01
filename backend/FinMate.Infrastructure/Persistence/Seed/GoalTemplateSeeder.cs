using System.Text.Json;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Mẫu mục tiêu ban đầu và việc phương pháp nào gợi ý mẫu nào ("mục tiêu gắn với từng phương pháp"). Phải chạy SAU
/// <see cref="FinanceMethodSeeder"/> vì bảng nối tham chiếu phương pháp.
///
/// Idempotent theo <c>code</c>. Liên kết phương pháp–mẫu CHỈ được gieo khi mẫu vừa được tạo trong lần chạy này: admin gỡ một
/// liên kết (xóa dòng trong <c>method_goal_templates</c>) thì lần khởi động sau không được âm thầm thêm lại nó. Hệ quả: thêm một
/// liên kết mới cho mẫu ĐÃ có phải làm bằng migration hoặc qua admin, không phải sửa mảng <c>Links</c> ở đây.
///
/// <c>conditions</c> = <c>{"excludes":[signal]}</c>: mẫu KHÔNG được gợi ý khi người dùng có một trong các signal đó. Mọi mẫu tiết kiệm
/// đều loại người đang thiếu hụt dòng tiền hoặc trễ nghĩa vụ — file .md mục 3: "Chỉ đặt mức đóng góp sau khi xét chi thiết yếu và nghĩa vụ".
/// Số tiền cụ thể KHÔNG nằm trong mẫu: khảo sát không hỏi số tiền, nên người dùng tự nhập.
/// </summary>
public static class GoalTemplateSeeder
{
    private sealed record TemplateSeed(string Code, string Name, string Description, string DefaultParams, string[] Excludes);

    private static readonly TemplateSeed[] Templates =
    [
        new(GoalTemplateCodes.StarterEmergencyBuffer,
            "Quỹ đệm khởi đầu",
            "Khoản dự phòng nhỏ, khoảng một tuần chi phí thiết yếu, để không phải vay khi có chi bất ngờ.",
            """{"essential_weeks":1}""",
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations]),

        new(GoalTemplateCodes.EmergencyFundThreeMonths,
            "Quỹ khẩn cấp 3 tháng",
            "Quỹ dự phòng lớn hơn, bằng khoảng ba tháng chi phí thiết yếu, tích lũy dần sau khi đã có quỹ đệm.",
            """{"essential_months":3}""",
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.DebtPressure, FinanceSignals.CannotSaveNow]),

        new(GoalTemplateCodes.IrregularExpensesFund,
            "Quỹ chi phí không đều",
            "Để dành dần cho khoản đã biết trước nhưng không hằng tháng, như bảo hiểm, học phí hay sửa xe.",
            """{"income_pct":5}""",
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations]),

        new(GoalTemplateCodes.SpecificTargetGoal,
            "Mục tiêu cụ thể",
            "Bạn tự đặt tên, số tiền và thời hạn, ví dụ mua một chiếc laptop.",
            "{}",
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.CannotSaveNow]),
    ];

    /// <summary>(mã phương pháp, mã mẫu, thứ tự gợi ý). Phương pháp theo dõi tối giản và trả nợ cố ý không có mẫu nào — không đẩy tiết kiệm lúc đó.</summary>
    private static readonly (string Method, string Template, int Order)[] Links =
    [
        (FinanceMethodCodes.EmergencyBuffer, GoalTemplateCodes.StarterEmergencyBuffer, 1),
        (FinanceMethodCodes.EmergencyBuffer, GoalTemplateCodes.EmergencyFundThreeMonths, 2),
        (FinanceMethodCodes.EmergencyBuffer, GoalTemplateCodes.IrregularExpensesFund, 3),
        (FinanceMethodCodes.GoalFund, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.GoalFund, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.CategoryBudget, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.CategoryBudget, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.AvailableToSpend, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.AvailableToSpend, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.CashFlowBudget, GoalTemplateCodes.StarterEmergencyBuffer, 1),
        (FinanceMethodCodes.CashFlowBudget, GoalTemplateCodes.IrregularExpensesFund, 2),
        (FinanceMethodCodes.EnvelopeBudget, GoalTemplateCodes.IrregularExpensesFund, 1),
        (FinanceMethodCodes.EnvelopeBudget, GoalTemplateCodes.SpecificTargetGoal, 2),
        (FinanceMethodCodes.LowEffortAutomation, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.LowEffortAutomation, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.SixJars, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.SixJars, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.Rule702010, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.Rule702010, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.Rule8020, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.Rule8020, GoalTemplateCodes.StarterEmergencyBuffer, 2),
        (FinanceMethodCodes.ZeroBased, GoalTemplateCodes.SpecificTargetGoal, 1),
        (FinanceMethodCodes.ZeroBased, GoalTemplateCodes.StarterEmergencyBuffer, 2),
    ];

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var templateIds = await context.GoalTemplates.ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var methodIds = await context.FinanceMethods.ToDictionaryAsync(m => m.Code, m => m.Id, ct);

        var now = DateTimeOffset.UtcNow;
        var created = new Dictionary<string, TemplateSeed>();

        foreach (var seed in Templates)
        {
            if (templateIds.ContainsKey(seed.Code))
            {
                continue;
            }

            var id = Guid.NewGuid();
            templateIds[seed.Code] = id;
            created[seed.Code] = seed;
            context.GoalTemplates.Add(new GoalTemplate
            {
                Id = id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                DefaultParams = seed.DefaultParams,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        foreach (var (method, template, order) in Links)
        {
            if (!created.TryGetValue(template, out var seed))
            {
                continue;
            }

            if (!methodIds.TryGetValue(method, out var methodId))
            {
                throw new InvalidOperationException(
                    $"Phương pháp '{method}' chưa có trong DB: chạy FinanceMethodSeeder trước GoalTemplateSeeder.");
            }

            context.MethodGoalTemplates.Add(new MethodGoalTemplate
            {
                MethodId = methodId,
                TemplateId = templateIds[template],
                Conditions = JsonSerializer.Serialize(new { excludes = seed.Excludes }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                SortOrder = order,
                CreatedAt = now,
            });
        }

        if (created.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
