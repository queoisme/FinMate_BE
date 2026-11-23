using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

/// <param name="CategoryId">Id danh mục hệ thống — app gửi thẳng vào <c>POST /forecast/categories/apply</c> khi người dùng áp dụng.</param>
public sealed record FinancialPlanLineDto(
    Guid CategoryId, string Slug, string Name, string? GroupKey, long FixedVnd, long DeclaredVnd, long LimitVnd, long ReducedByVnd);

/// <param name="CapVnd">Trần của nhóm chi (thu nhập × %); null với nhóm "phần còn lại" và nhóm để dành.</param>
public sealed record FinancialPlanGroupDto(string? Key, string Label, string Kind, int? Pct, long? CapVnd);

/// <param name="Warnings">Mã cảnh báo: <c>deficit</c>, <c>below_save_target</c>, <c>fixed_exceeds_group:&lt;key&gt;</c>.</param>
/// <param name="WarningTexts">Chữ hiển thị theo mã, lấy từ cấu hình hệ thống (<c>financeMethods.planWarningTexts</c>), có cả mã app tự tính lại khi người dùng sửa hạn mức.</param>
public sealed record FinancialPlanDto(
    string MethodCode,
    int MethodVersion,
    long IncomeVnd,
    long FixedVnd,
    long FixedOtherVnd,
    long PlannedSpendVnd,
    long SurplusVnd,
    long SaveTargetVnd,
    long? SavingsBalanceVnd,
    IReadOnlyList<FinancialPlanLineDto> Categories,
    IReadOnlyList<FinancialPlanGroupDto> Groups,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string> WarningTexts);

public interface IFinancialPlanService
{
    /// <summary>Bản nháp kế hoạch tháng từ khảo sát + phương pháp chính đang dùng. Không lưu gì.</summary>
    Task<FinancialPlanDto> GetDraftAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Ráp dữ liệu cho <see cref="FinancialPlanCalculator"/> (đặc tả 2026-11-23-onboarding-financial-plan-design.md §6–7): thu nhập từ hồ sơ (câu <c>income</c> ghi vào),
/// khoản cố định và chi linh hoạt từ câu trả lời khảo sát theo <c>plan_role</c>, bảng phân bổ và % hiệu lực từ phương pháp chính. Số tiền không bao giờ được log.
/// </summary>
public class FinancialPlanService : IFinancialPlanService
{
    private const long Rounding = 1_000;

    private readonly ISurveyRepository _surveys;
    private readonly IUserMethodRepository _userMethods;
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserRepository _users;
    private readonly ICategoryRepository _categories;
    private readonly ISystemConfigProvider _config;

    public FinancialPlanService(
        ISurveyRepository surveys,
        IUserMethodRepository userMethods,
        IFinanceMethodRepository methods,
        IUserRepository users,
        ICategoryRepository categories,
        ISystemConfigProvider config)
    {
        _config = config;
        _surveys = surveys;
        _userMethods = userMethods;
        _methods = methods;
        _users = users;
        _categories = categories;
    }

    public async Task<FinancialPlanDto> GetDraftAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        if (user.MonthlyIncomeCents is not (> 0 and var income))
        {
            throw new BusinessRuleException(FinanceErrorCodes.PlanNoIncome, "Chưa có thu nhập hằng tháng để lập kế hoạch. Hãy nhập thu nhập của bạn.");
        }

        var primary = (await _userMethods.GetActiveAsync(userId, ct)).FirstOrDefault(m => m.Role == UserMethodRole.Primary);
        var method = primary is null ? null : await _methods.GetMethodByIdAsync(primary.MethodId, ct);
        if (primary is null || method is null)
        {
            throw new BusinessRuleException(FinanceErrorCodes.PlanNoActiveMethod, "Hãy chọn một cách quản lý tài chính trước khi lập kế hoạch.");
        }

        // Câu đã tắt vẫn tính: admin tắt một câu sau khi người dùng đã trả lời thì câu trả lời vẫn là thông tin thật về họ.
        var questions = (await _surveys.GetQuestionsAsync(includeInactive: true, ct)).ToDictionary(q => q.Code);
        var options = (await _surveys.GetOptionsAsync(includeInactive: true, ct)).ToDictionary(o => (o.QuestionId, o.Code));
        var systemCategories = (await _categories.GetSystemListAsync(includeInactive: false, ct)).ToDictionary(c => c.Slug);

        var fixedByCategory = new Dictionary<string, long>();
        var declared = new Dictionary<string, long>();
        long fixedOther = 0;
        long? savingsBalance = null;
        foreach (var answer in await _surveys.GetAnswersAsync(userId, ct))
        {
            if (!questions.TryGetValue(answer.QuestionCode, out var question) || question.PlanRole is null)
            {
                continue;
            }

            var slug = question.CategorySlug;
            switch (question.PlanRole)
            {
                case SurveyPlanRoles.Fixed when answer.AmountVnd is { } amount:
                    if (slug is null)
                    {
                        fixedOther += amount;
                    }
                    else if (systemCategories.ContainsKey(slug))
                    {
                        fixedByCategory[slug] = fixedByCategory.GetValueOrDefault(slug) + amount;
                    }

                    break;
                case SurveyPlanRoles.Flexible when slug is not null && systemCategories.ContainsKey(slug)
                                                   && options.TryGetValue((question.Id, answer.OptionCode), out var option)
                                                   && option.ShareMinPct is { } min:
                    var pct = option.ShareMaxPct is { } max ? (min + max) / 2m : min;
                    declared[slug] = declared.GetValueOrDefault(slug) + (long)(income * pct / 100m) / Rounding * Rounding;
                    break;
                case SurveyPlanRoles.SavingsBalance when answer.AmountVnd is { } balance:
                    savingsBalance = balance;
                    break;
            }
        }

        var defs = MethodParams.ParseDefs(method.ParamDefs);
        var effective = MethodParams.Effective(defs, MethodParams.ParseOverrides(primary.Params));
        var groups = ReadGroups(method.Allocation, effective);

        var plan = FinancialPlanCalculator.Calculate(new PlanInput(income, fixedByCategory, fixedOther, declared, groups));

        return new FinancialPlanDto(
            method.Code,
            method.Version,
            plan.IncomeVnd,
            plan.FixedVnd,
            fixedOther,
            plan.PlannedSpendVnd,
            plan.SurplusVnd,
            plan.SaveTargetVnd,
            savingsBalance,
            plan.Categories.Select(l => new FinancialPlanLineDto(
                systemCategories[l.Slug].Id, l.Slug, systemCategories[l.Slug].Name, l.GroupKey, l.FixedVnd, l.DeclaredVnd, l.LimitVnd, l.ReducedByVnd)).ToList(),
            groups.Select(g => new FinancialPlanGroupDto(
                g.Key, g.Label, g.Kind, g.Pct,
                g.Kind == AllocationKinds.Spend && g.Pct is { } pct ? income * pct / 100 / Rounding * Rounding : null)).ToList(),
            plan.Warnings,
            WarningTexts((await _config.GetAsync(ct)).Document.FinanceMethods.PlanWarningTexts, groups));
    }

    private static Dictionary<string, string> WarningTexts(IReadOnlyDictionary<string, string> configured, IReadOnlyList<PlanGroup> groups)
    {
        var texts = new Dictionary<string, string>();
        foreach (var code in new[] { FinancialPlanCalculator.WarningDeficit, FinancialPlanCalculator.WarningBelowSaveTarget })
        {
            if (configured.TryGetValue(code, out var text))
            {
                texts[code] = text;
            }
        }

        const string fixedCode = "fixed_exceeds_group";
        if (configured.TryGetValue(fixedCode, out var template))
        {
            foreach (var group in groups.Where(g => g.Kind == AllocationKinds.Spend && g.Key is not null))
            {
                texts[FinancialPlanCalculator.WarningFixedExceedsGroupPrefix + group.Key] = template.Replace("{group}", group.Label);
            }
        }

        return texts;
    }

    /// <summary>Đọc bảng phân bổ; nhóm trỏ tới tham số không còn (admin đã xóa) thì bỏ % của nó — nhóm thành không trần thay vì làm hỏng cả kế hoạch.</summary>
    private static List<PlanGroup> ReadGroups(string allocationJson, IReadOnlyDictionary<string, long> effective)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(allocationJson) ? "[]" : allocationJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var groups = new List<PlanGroup>();
        foreach (var group in doc.RootElement.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object))
        {
            var key = group.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            var label = group.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : string.Empty;
            var kind = group.TryGetProperty("kind", out var kd) && kd.ValueKind == JsonValueKind.String ? kd.GetString()! : AllocationKinds.Spend;
            var categories = group.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];
            int? pct = key is not null && effective.TryGetValue(key, out var value) ? (int)value : null;
            groups.Add(new PlanGroup(pct is null ? null : key, label, kind, categories, pct));
        }

        return groups;
    }
}
