using System.Text.Json;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Bộ 10 câu khảo sát đầu vào (nguồn: finmate_khao_sat_insight_va_goi_y_quan_ly_tai_chinh.md, mục 2) và ánh xạ
/// "đáp án → signals" ở mục 3. Idempotent theo <c>code</c>: chạy lại mỗi lần khởi động chỉ thêm câu
/// hỏi/đáp án CHƯA có, không đụng bản admin đã sửa hay tắt.
///
/// Trọng số 1–3: 3 = dấu hiệu rõ, 1 = dấu hiệu nhẹ. Đáp án "bỏ qua" không có dòng nào — câu bỏ qua không được tính điểm.
/// </summary>
public static class SurveySeeder
{
    private const string GroupCashFlow = "cash_flow";
    private const string GroupHabits = "habits";
    private const string GroupGoals = "goals";
    private const string GroupExperience = "experience";

    private const string QIncome = "income_pattern";
    private const string QMonthEnd = "month_end_balance";
    private const string QShock = "unexpected_expense";
    private const string QTracking = "tracking_habit";
    private const string QLimits = "limit_reaction";
    private const string QHardest = "hardest_category";
    private const string QGoal = "main_goal";
    private const string QSaving = "saving_style";
    private const string QTime = "time_budget";
    private const string QSupport = "support_style";
    private const string QSpend = "avg_monthly_spend";

    private const string GroupMoney = "money";

    private sealed record OptionSeed(string Code, string Text, (string Signal, int Weight)[] Signals, int? ShareMinPct = null, int? ShareMaxPct = null);

    private sealed record QuestionSeed(
        string Code, string Group, string Text, string? ShowIf, OptionSeed[] Options,
        string AnswerType = SurveyAnswerTypes.Choice, string? CategorySlug = null, string? PlanRole = null);

    /// <summary>Câu nhập số tiền cho kế hoạch tháng (không có đáp án, không sinh signal).</summary>
    private static QuestionSeed Amount(string code, string text, string planRole, string? categorySlug = null)
        => new(code, GroupMoney, text, null, [], SurveyAnswerTypes.Amount, categorySlug, planRole);

    /// <summary>
    /// Câu "thường chi cho X khoảng bao nhiêu": đáp án là khoảng % thu nhập (app đổi ra số tiền). Kế hoạch dùng điểm giữa khoảng.
    /// Không sinh signal — mức chi không ảnh hưởng việc chọn phương pháp.
    /// </summary>
    private static QuestionSeed Flexible(string categorySlug, string label)
        => new($"flex_{categorySlug}", GroupMoney, $"Mỗi tháng bạn thường chi cho {label} khoảng bao nhiêu?", null,
        [
            new("A", "Không chi", [], 0, 0),
            new("B", "Dưới 5% thu nhập", [], 0, 5),
            new("C", "Từ 5% đến 10%", [], 5, 10),
            new("D", "Từ 10% đến 20%", [], 10, 20),
            new("E", "Từ 20% đến 30%", [], 20, 30),
            new("F", "Từ 30% trở lên", [], 30, null),
        ], SurveyAnswerTypes.Choice, categorySlug, SurveyPlanRoles.Flexible);

    /// <summary>
    /// Định dạng <c>show_if</c>: <c>{"all":[{"question":mã,"in":[mã đáp án]} | {"question":mã,"not_in":[mã đáp án]}]}</c>.
    /// Câu cách để dành chỉ hỏi khi mục tiêu là tiết kiệm VÀ người dùng không THƯỜNG XUYÊN thiếu hụt dòng tiền (file .md mục 6, bước 3:
    /// thiếu tiền thì đưa ổn định dòng tiền lên trước, không đẩy tiết kiệm). "Thỉnh thoảng thiếu" (D) vẫn được hỏi — ví dụ trong file .md (2D, 7B, 8C)
    /// chính là người như vậy và vẫn nhận quỹ dự phòng nhỏ.
    /// </summary>
    private static readonly string SavingStyleShowIf =
        $$"""{"all":[{"question":"{{QGoal}}","in":["B","D"]},{"question":"{{QMonthEnd}}","not_in":["E"]}]}""";

    private static readonly QuestionSeed[] Questions =
    [
        new(QIncome, GroupCashFlow, "Thu nhập của bạn thường đến theo cách nào?", null,
        [
            new("A", "Một khoản cố định, đúng kỳ hằng tháng", [(FinanceSignals.IncomeStable, 3)]),
            new("B", "Cố định nhưng ngày nhận có thể lệch", [(FinanceSignals.IncomeStable, 2), (FinanceSignals.IncomeVariable, 1)]),
            new("C", "Nhiều nguồn, tổng thu thay đổi theo tháng", [(FinanceSignals.IncomeVariable, 2)]),
            new("D", "Theo dự án, mùa vụ hoặc đợt không đều", [(FinanceSignals.IncomeVariable, 3)]),
            new("E", "Hiện chưa có thu nhập ổn định", [(FinanceSignals.NoStableIncome, 3), (FinanceSignals.IncomeVariable, 2)]),
        ]),
        // Câu về mức chi: không tạo signal nào (không ảnh hưởng việc chọn cách quản lý). Mức tiền ứng với từng khoảng nằm ở cấu hình agent.surveySpendEstimate; dự báo sơ khảo dùng nó khi chưa có chi tiêu thật.
        new(QSpend, GroupCashFlow, "Trung bình mỗi tháng bạn tiêu khoảng bao nhiêu?", null,
        [
            new("A", "Dưới 5 triệu", []),
            new("B", "Từ 5 đến dưới 10 triệu", []),
            new("C", "Từ 10 đến dưới 15 triệu", []),
            new("D", "Từ 15 đến dưới 25 triệu", []),
            new("E", "Từ 25 triệu trở lên", []),
        ]),
        new(QMonthEnd, GroupCashFlow, "Cuối tháng, tình hình tiền của bạn thường thế nào?", null,
        [
            new("A", "Còn dư đều đặn", [(FinanceSignals.SurplusRegular, 3)]),
            new("B", "Có dư nhưng không biết đã tiêu vào đâu", [(FinanceSignals.SurplusRegular, 1), (FinanceSignals.LowVisibility, 2)]),
            new("C", "Vừa đủ, ít dư", [(FinanceSignals.TightCashFlow, 2)]),
            new("D", "Thỉnh thoảng thiếu, phải dùng tiết kiệm hoặc vay", [(FinanceSignals.OccasionalShortfall, 2), (FinanceSignals.TightCashFlow, 1)]),
            new("E", "Thường xuyên thiếu hoặc trễ khoản phải trả", [(FinanceSignals.CashShortfall, 3), (FinanceSignals.OverdueObligations, 2)]),
        ]),
        new(QShock, GroupCashFlow, "Nếu có một khoản chi bất ngờ tương đương một tuần chi phí thiết yếu, bạn sẽ làm gì?", null,
        [
            new("A", "Dùng quỹ dự phòng riêng", [(FinanceSignals.HasBuffer, 3)]),
            new("B", "Dùng tiền tiết kiệm nhưng chưa tách quỹ", [(FinanceSignals.HasBuffer, 1), (FinanceSignals.BufferUnseparated, 2)]),
            new("C", "Cắt hoặc hoãn khoản chi khác", [(FinanceSignals.ThinBuffer, 2)]),
            new("D", "Vay hoặc dùng thẻ tín dụng", [(FinanceSignals.ThinBuffer, 2), (FinanceSignals.DebtPressure, 2)]),
            new("E", "Chưa biết sẽ xoay xở thế nào", [(FinanceSignals.ThinBuffer, 3)]),
        ]),
        new(QHardest, GroupHabits, "Khoản nào khiến bạn khó kiểm soát nhất?", null,
        [
            new("A", "Ăn uống, mua sắm hoặc giải trí hằng ngày", [(FinanceSignals.FlexibleSpendLeak, 3)]),
            new("B", "Hóa đơn, thuê nhà và các khoản cố định", [(FinanceSignals.FixedCostsPressure, 3)]),
            new("C", "Khoản phát sinh không định kỳ", [(FinanceSignals.IrregularExpenses, 3)]),
            new("D", "Trả nợ hoặc lãi vay", [(FinanceSignals.DebtPressure, 3)]),
            new("E", "Chưa biết khoản nào là nguyên nhân", [(FinanceSignals.LowVisibility, 2)]),
        ]),
        new(QTracking, GroupHabits, "Bạn hiện theo dõi chi tiêu bằng cách nào?", null,
        [
            new("A", "Ghi gần như mọi giao dịch", [(FinanceSignals.TracksClosely, 3)]),
            new("B", "Xem ứng dụng ngân hàng, tổng kết định kỳ", [(FinanceSignals.TracksViaBank, 2)]),
            new("C", "Chỉ kiểm tra số dư khi cần", [(FinanceSignals.LowVisibility, 2)]),
            new("D", "Đã thử ghi chép nhưng nhanh bỏ cuộc", [(FinanceSignals.AbandonsTracking, 3)]),
            new("E", "Hầu như không theo dõi", [(FinanceSignals.LowVisibility, 3)]),
        ]),
        new(QLimits, GroupHabits, "Khi đặt giới hạn chi tiêu, bạn thường phản ứng thế nào?", null,
        [
            new("A", "Thấy hữu ích và thường làm theo", [(FinanceSignals.LikesLimits, 3)]),
            new("B", "Làm được nếu giới hạn có thể điều chỉnh", [(FinanceSignals.NeedsFlexibleLimits, 3)]),
            new("C", "Chỉ muốn giới hạn vài khoản dễ vượt", [(FinanceSignals.SelectiveLimits, 3)]),
            new("D", "Dễ thấy bị gò bó rồi bỏ cuộc", [(FinanceSignals.DislikesLimits, 3)]),
            new("E", "Chưa từng thử", [(FinanceSignals.UntriedLimits, 2)]),
        ]),
        new(QGoal, GroupGoals, "Mục tiêu tài chính quan trọng nhất trong 6–12 tháng tới là gì?", null,
        [
            new("A", "Biết tiền đi đâu và tránh hụt cuối tháng", [(FinanceSignals.GoalCashFlowControl, 3)]),
            new("B", "Tạo hoặc tăng quỹ dự phòng", [(FinanceSignals.GoalEmergencyFund, 3)]),
            new("C", "Giảm nợ", [(FinanceSignals.GoalDebtReduction, 3), (FinanceSignals.DebtPressure, 1)]),
            new("D", "Dành tiền cho một mục tiêu cụ thể", [(FinanceSignals.GoalSpecificTarget, 3)]),
            new("E", "Chưa rõ mục tiêu", [(FinanceSignals.GoalUnclear, 3)]),
        ]),
        new(QSaving, GroupGoals, "Bạn muốn dành tiền cho mục tiêu theo cách nào?", SavingStyleShowIf,
        [
            new("A", "Chuyển tự động ngay khi nhận thu nhập", [(FinanceSignals.SavesAutomatically, 3)]),
            new("B", "Để riêng một khoản cố định mỗi kỳ", [(FinanceSignals.SavesFixedAmount, 3)]),
            new("C", "Để riêng theo tỷ lệ thu nhập thực nhận", [(FinanceSignals.SavesPercentOfIncome, 3)]),
            new("D", "Chỉ để dành phần còn lại cuối kỳ", [(FinanceSignals.SavesLeftover, 3)]),
            new("E", "Hiện chưa thể để dành", [(FinanceSignals.CannotSaveNow, 3)]),
        ]),
        new(QTime, GroupExperience, "Bạn sẵn sàng dành bao nhiêu thời gian cho việc quản lý tiền?", null,
        [
            new("A", "Vài phút mỗi ngày", [(FinanceSignals.TimeDaily, 3)]),
            new("B", "15–30 phút mỗi tuần", [(FinanceSignals.TimeWeekly, 3)]),
            new("C", "Một lần mỗi tháng", [(FinanceSignals.TimeMonthly, 3)]),
            new("D", "Chỉ khi có thông báo quan trọng", [(FinanceSignals.NotificationsOnly, 3)]),
            new("E", "Chưa chắc có thể duy trì", [(FinanceSignals.LowCommitment, 3)]),
        ]),
        new(QSupport, GroupExperience, "Bạn muốn FinMate hỗ trợ theo cách nào nhất?", null,
        [
            new("A", "Đưa hạn mức cụ thể cho từng nhóm chi", [(FinanceSignals.WantsCategoryLimits, 3)]),
            new("B", "Chỉ cho biết còn bao nhiêu tiền có thể chi", [(FinanceSignals.WantsSingleNumber, 3)]),
            new("C", "Nhắc hóa đơn và cảnh báo khi có nguy cơ thiếu tiền", [(FinanceSignals.WantsBillAlerts, 3)]),
            new("D", "Theo dõi tiến độ mục tiêu và động viên", [(FinanceSignals.WantsGoalTracking, 3)]),
            new("E", "Cho tôi tự xem số liệu và quyết định", [(FinanceSignals.WantsSelfService, 3)]),
        ]),

        // Phần số liệu cho kế hoạch tháng (đặc tả 2026-11-23-onboarding-financial-plan-design.md §4). Đứng SAU các câu chọn phương pháp.
        Amount("income_amount", "Mỗi tháng bạn thực nhận khoảng bao nhiêu?", SurveyPlanRoles.Income),
        Amount("fixed_housing", "Tiền nhà hoặc tiền thuê trọ mỗi tháng", SurveyPlanRoles.Fixed, "housing"),
        Amount("fixed_bills", "Điện, nước, internet, điện thoại mỗi tháng", SurveyPlanRoles.Fixed, "bills"),
        Amount("fixed_debt", "Trả góp hoặc trả nợ mỗi tháng", SurveyPlanRoles.Fixed),
        Flexible("food", "ăn uống"),
        Flexible("transport", "đi lại"),
        Flexible("shopping", "mua sắm"),
        Flexible("entertainment", "giải trí"),
        Flexible("health", "sức khỏe"),
        Flexible("education", "học tập"),
        Flexible("family", "gia đình"),
        Amount("savings_balance", "Hiện bạn đã có bao nhiêu tiền tiết kiệm?", SurveyPlanRoles.SavingsBalance),
    ];

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var questionIds = await context.SurveyQuestions.ToDictionaryAsync(q => q.Code, q => q.Id, ct);
        var optionKeys = (await context.SurveyOptions.Select(o => new { o.QuestionId, o.Code }).ToListAsync(ct))
            .Select(o => (o.QuestionId, o.Code))
            .ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var added = false;

        for (var i = 0; i < Questions.Length; i++)
        {
            var seed = Questions[i];
            if (!questionIds.TryGetValue(seed.Code, out var questionId))
            {
                questionId = Guid.NewGuid();
                questionIds[seed.Code] = questionId;
                context.SurveyQuestions.Add(new SurveyQuestion
                {
                    Id = questionId,
                    Code = seed.Code,
                    GroupCode = seed.Group,
                    Text = seed.Text,
                    ShowIf = seed.ShowIf,
                    AnswerType = seed.AnswerType,
                    CategorySlug = seed.CategorySlug,
                    PlanRole = seed.PlanRole,
                    SortOrder = i + 1,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                added = true;
            }

            for (var j = 0; j < seed.Options.Length; j++)
            {
                var option = seed.Options[j];
                if (optionKeys.Contains((questionId, option.Code)))
                {
                    continue;
                }

                context.SurveyOptions.Add(new SurveyOption
                {
                    Id = Guid.NewGuid(),
                    QuestionId = questionId,
                    Code = option.Code,
                    Text = option.Text,
                    Signals = JsonSerializer.Serialize(option.Signals.ToDictionary(s => s.Signal, s => s.Weight)),
                    ShareMinPct = option.ShareMinPct,
                    ShareMaxPct = option.ShareMaxPct,
                    SortOrder = j + 1,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                added = true;
            }
        }

        if (added)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
