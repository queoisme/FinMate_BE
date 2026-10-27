using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.SavingGoals;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public class GoalPlanningService : IGoalPlanningService
{
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserMethodRepository _userMethods;
    private readonly ISurveyRepository _surveys;
    private readonly IUserRepository _users;
    private readonly IBudgetRepository _budgets;
    private readonly ISavingGoalRepository _goals;
    private readonly IFinanceSnapshotRepository _snapshots;

    public GoalPlanningService(
        IFinanceMethodRepository methods,
        IUserMethodRepository userMethods,
        ISurveyRepository surveys,
        IUserRepository users,
        IBudgetRepository budgets,
        ISavingGoalRepository goals,
        IFinanceSnapshotRepository snapshots)
    {
        _methods = methods;
        _userMethods = userMethods;
        _surveys = surveys;
        _users = users;
        _budgets = budgets;
        _goals = goals;
        _snapshots = snapshots;
    }

    public async Task<IReadOnlyList<GoalSuggestionDto>> GetSuggestionsAsync(Guid userId, CancellationToken ct = default)
    {
        var active = (await _userMethods.GetActiveAsync(userId, ct)).OrderBy(a => a.Role).ToList();   // cách chính trước
        if (active.Count == 0)
        {
            return [];
        }

        var methods = (await _methods.GetMethodsByIdsAsync(active.Select(a => a.MethodId), ct)).ToDictionary(m => m.Id);
        var links = (await _methods.GetLinksForMethodsAsync(methods.Keys, ct)).ToLookup(l => l.MethodId);
        var templates = (await _methods.GetTemplatesAsync(includeInactive: false, ct)).ToDictionary(t => t.Id);

        // Signals của người dùng quyết định mẫu nào KHÔNG được gợi ý (vd đang thiếu hụt dòng tiền thì không đẩy tiết kiệm). Chưa làm khảo sát = không signal nào = không loại gì.
        var signals = SurveySignals.FromAnswers(
            await _surveys.GetAnswersAsync(userId, ct),
            await _surveys.GetQuestionsAsync(includeInactive: true, ct),
            await _surveys.GetOptionsAsync(includeInactive: true, ct));

        var suggestions = new List<GoalSuggestionDto>();
        var seen = new HashSet<string>();
        foreach (var userMethod in active.Where(a => methods.ContainsKey(a.MethodId)))
        {
            var method = methods[userMethod.MethodId];
            foreach (var link in links[method.Id].OrderBy(l => l.SortOrder))
            {
                if (!templates.TryGetValue(link.TemplateId, out var template) || !seen.Add(template.Code))
                {
                    continue;
                }

                if (FinanceJson.ReadStringList(link.Conditions, "excludes").Any(signals.ContainsKey))
                {
                    seen.Remove(template.Code);   // bị loại ở cách này nhưng cách kia vẫn có thể gợi ý được
                    continue;
                }

                suggestions.Add(new GoalSuggestionDto(
                    template.Code, template.Name, template.Description, FinanceJson.ToElement(template.DefaultParams), method.Code));
            }
        }

        return suggestions;
    }

    public async Task<SpendableDto> GetSpendableAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        long? income = user.MonthlyIncomeCents is > 0 ? user.MonthlyIncomeCents : null;

        var now = DateTimeOffset.UtcNow;
        var (start, end) = VietnamTime.MonthRange(now);
        var spent = await _snapshots.GetDebitTotalAsync(userId, start, end, ct);

        var primary = (await _userMethods.GetActiveAsync(userId, ct)).FirstOrDefault(a => a.Role == UserMethodRole.Primary);
        var method = primary is null ? null : await _methods.GetMethodByIdAsync(primary.MethodId, ct);
        var mode = method?.GoalMode;

        var deposits = await _snapshots.GetGoalDepositsAsync(userId, start, end, ct);
        var goals = new List<SpendableGoalDto>();
        foreach (var goal in await _goals.GetListForUserAsync(userId, SavingGoalStatus.Active, ct))
        {
            var remaining = Math.Max(0, goal.TargetCents - goal.SavedCents);
            var reservesUpfront = mode is GoalMode.ReserveFirst or GoalMode.Envelope;
            var required = reservesUpfront && goal.Deadline is { } deadline && remaining > 0
                ? GoalFeasibilityCalculator.RequiredPerMonth(remaining, deadline, now)
                : 0;
            var deposited = deposits.GetValueOrDefault(goal.Id);

            goals.Add(new SpendableGoalDto(goal.Id, goal.Name, required, deposited, SetAside(mode, required, deposited)));
        }

        var setAside = goals.Sum(g => g.SetAsideCents);
        return new SpendableDto(
            mode is null ? null : FinanceMapper.GoalModeText(mode.Value),
            income,
            spent,
            setAside,
            income - spent - setAside,   // null khi chưa khai thu nhập: "chưa biết" không phải "bằng 0"
            goals);
    }

    public async Task<GoalFeasibilityDto?> PreviewFeasibilityAsync(
        Guid userId, long targetCents, long savedCents, DateTimeOffset deadline, CancellationToken ct = default)
    {
        if (targetCents <= 0 || savedCents < 0)
        {
            throw new BusinessRuleException(FinanceErrorCodes.FeasibilityInputInvalid, "Số tiền mục tiêu phải lớn hơn 0 và số đã có không được âm.");
        }

        var remaining = Math.Max(0, targetCents - savedCents);
        if (remaining == 0)
        {
            return null;
        }

        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        var income = user.MonthlyIncomeCents;

        // Giống GetGoalProgressQueryHandler: chỉ nạp ngân sách khi đã biết thu nhập.
        var budgetLimits = income is > 0
            ? (await _budgets.GetListForUserAsync(userId, ct)).Sum(b => b.LimitCents)
            : 0;

        return GoalFeasibilityCalculator.Compute(remaining, savedCents, deadline, DateTimeOffset.UtcNow, income, budgetLimits);
    }

    /// <summary>
    /// Khoản dành cho một mục tiêu trong tháng theo cách phương pháp đưa tiền để dành vào dòng tiền:
    /// <list type="bullet">
    /// <item><b>reserve_first / envelope</b>: giữ TRƯỚC phần cần góp mỗi tháng — hoặc số đã nạp nếu nhiều hơn, để nạp xong không bị trừ hai lần.</item>
    /// <item><b>available_to_spend</b>: chỉ trừ số ĐÃ nạp.</item>
    /// <item><b>track_only</b> hoặc chưa dùng phương pháp: mục tiêu không ảnh hưởng tiền khả dụng.</item>
    /// </list>
    /// </summary>
    private static long SetAside(GoalMode? mode, long required, long deposited) => mode switch
    {
        GoalMode.ReserveFirst or GoalMode.Envelope => Math.Max(required, deposited),
        GoalMode.AvailableToSpend => deposited,
        _ => 0,
    };
}
