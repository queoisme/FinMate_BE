using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Agent;

/// <summary>Căn cứ của ước tính sơ khảo, theo thứ tự ưu tiên: chi tiêu thật đã ghi, phần tiết kiệm của cách quản lý đã chọn, câu trả lời khảo sát ban đầu.</summary>
public enum CapacityBasis
{
    None,
    Spend,
    Method,
    Survey,
    SurveySpend,
}

/// <param name="Capacity">Số dành được mỗi tháng; null khi chưa có căn cứ nào.</param>
/// <param name="SpendMonths">Số tháng chi tiêu đã ghi (căn cứ Spend).</param>
/// <param name="AverageSpend">Chi trung bình mỗi tháng (căn cứ Spend).</param>
/// <param name="MethodName">Tên cách quản lý (căn cứ Method).</param>
/// <param name="Percent">Phần trăm thu nhập dành để tiết kiệm (căn cứ Method hoặc Survey).</param>
public sealed record PreliminaryCapacity(long Income, long? Capacity, CapacityBasis Basis, int SpendMonths, long AverageSpend, string? MethodName, long Percent);

/// <summary>
/// Khả năng dành được mỗi tháng khi lịch sử còn ngắn (dưới <see cref="AgentLimits.MinHistoryMonths"/> tháng), dùng chung cho <c>goal_plan</c> và <c>forecast_goal</c>.
/// Cần thu nhập người dùng đã khai. Căn cứ: (1) chi tiêu thật đã ghi: thu khai trừ chi trung bình; (2) cách quản lý chính (đã chọn từ khảo sát): tổng các khóa trong <c>agent.savingParamKeys</c>;
/// (3) câu trả lời khảo sát ban đầu (đã lưu ở <c>user_survey_answers</c>): phần trăm ứng với đáp án trong <c>agent.surveySavingShare</c>, lấy mức THẤP NHẤT nếu nhiều câu có mức.
/// Mọi phần trăm đều do admin cấu hình, code không giữ con số nào.
/// </summary>
public sealed class PreliminaryCapacityService(IUserRepository users, IUserMethodService methods, ISurveyRepository surveys, ISystemConfigProvider config)
{
    public async Task<PreliminaryCapacity?> GetAsync(Guid userId, MonthlySeries series, CancellationToken ct = default)
    {
        var income = (await users.GetByIdAsync(userId, ct))?.MonthlyIncomeCents ?? 0;
        if (income <= 0)
        {
            return null;
        }

        var spendMonths = series.SpendVnd.Where(v => v > 0).ToArray();
        if (spendMonths.Length > 0)
        {
            var average = spendMonths.Sum() / spendMonths.Length;
            return new PreliminaryCapacity(income, income - average, CapacityBasis.Spend, spendMonths.Length, average, null, 0);
        }

        var settings = (await config.GetAsync(ct)).Document.Agent;
        IReadOnlyList<UserSurveyAnswer> answers = settings.SurveySpendEstimate.Count + settings.SurveySavingShare.Count == 0 ? [] : await surveys.GetAnswersAsync(userId, ct);
        if (SurveySpend(answers, settings) is { } declaredSpend)
        {
            return new PreliminaryCapacity(income, income - declaredSpend, CapacityBasis.SurveySpend, 0, declaredSpend, null, 0);
        }

        if (await MethodSavingAsync(userId, settings, ct) is var (name, methodPercent) && methodPercent > 0)
        {
            return new PreliminaryCapacity(income, income * methodPercent / 100, CapacityBasis.Method, 0, 0, name, methodPercent);
        }

        return SurveySaving(answers, settings) is { } surveyPercent
            ? new PreliminaryCapacity(income, income * surveyPercent / 100, CapacityBasis.Survey, 0, 0, null, surveyPercent)
            : new PreliminaryCapacity(income, null, CapacityBasis.None, 0, 0, null, 0);
    }

    private async Task<(string Name, long Percent)?> MethodSavingAsync(Guid userId, FinMate.Application.Admin.SystemConfig.AgentSettings settings, CancellationToken ct)
    {
        var primary = (await methods.GetMineAsync(userId, ct)).FirstOrDefault(m => string.Equals(m.Role, nameof(UserMethodRole.Primary), StringComparison.OrdinalIgnoreCase));
        if (primary is null)
        {
            return null;
        }

        var matched = settings.SavingParamKeys.Where(primary.Effective.ContainsKey).ToList();
        return matched.Count == 0 ? null : (primary.Method.Name, Math.Min(100, matched.Sum(k => primary.Effective[k])));
    }

    /// <summary>Mức chi trung bình mỗi tháng người dùng chọn trong khảo sát (theo <c>agent.surveySpendEstimate</c>); null khi chưa trả lời hoặc admin chưa nhập mức.</summary>
    private static long? SurveySpend(IReadOnlyList<UserSurveyAnswer> answers, FinMate.Application.Admin.SystemConfig.AgentSettings settings)
        => answers.Select(a => settings.SurveySpendEstimate.TryGetValue($"{a.QuestionCode}:{a.OptionCode}", out var spend) ? (long?)spend : null).FirstOrDefault(v => v is not null);

    private static long? SurveySaving(IReadOnlyList<UserSurveyAnswer> answers, FinMate.Application.Admin.SystemConfig.AgentSettings settings)
    {
        var shares = answers
            .Select(a => settings.SurveySavingShare.TryGetValue($"{a.QuestionCode}:{a.OptionCode}", out var percent) ? (long?)percent : null)
            .Where(p => p is not null).Select(p => p!.Value).ToList();
        return shares.Count == 0 ? null : shares.Min();
    }
}
