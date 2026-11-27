using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Reports;
using FinMate.Domain.Enums;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>goal_plan</c>: "mục tiêu tiết kiệm <c>target_vnd</c> trong <c>months</c> tháng có khả thi không, mỗi tháng cần góp bao nhiêu, nếu không thì kéo dài bao lâu?".
/// Số cần góp = ⌈mục tiêu / số tháng⌉ (mục tiêu mới, chưa góp đồng nào). So với khả năng góp THẬT của người dùng đọc từ lịch sử thu chi: bảo thủ = thu Q10 − chi Q90, cơ sở = thu Q50 − chi Q50
/// (cùng hai kịch bản của <see cref="GoalSimulator"/>). Kết luận: <b>feasible</b> nếu cần góp ≤ mức bảo thủ; <b>tight</b> nếu ≤ mức cơ sở; còn lại <b>infeasible</b>.
/// Chưa khả thi thì tính tối đa 2 phương án KÉO DÀI hạn, làm tròn lên số năm tròn (infeasible dùng mức cơ sở, tight dùng mức bảo thủ để có vùng đệm), mỗi phương án kèm số cần góp mỗi tháng,
/// và sinh thẻ đổi hạn (mục tiêu cùng số tiền đã có) hoặc tạo mục tiêu (chưa có). Mọi con số do code tính; không giả định giá tăng.
///
/// <b>Sơ khảo</b> khi lịch sử chưa đủ <see cref="AgentLimits.MinHistoryMonths"/> tháng: dùng THU NHẬP HẰNG THÁNG người dùng đã khai (hồ sơ) thay cho lịch sử thu, và chi tiêu trung bình của các tháng đã ghi (nếu có).
/// Có chi tiêu thì so số cần góp với (thu khai − chi trung bình): <c>tight</c> nếu đủ, <c>infeasible</c> nếu không (không bao giờ <c>feasible</c> khi dữ liệu còn mỏng). Chưa có chi tiêu thì chỉ nêu số cần góp và phần trăm thu nhập khai,
/// không kết luận. Luôn kèm một bằng chứng nhắc đây là ước tính sơ bộ và cần ghi chép thêm để tính chính xác. Không khai thu nhập và không có lịch sử thì vẫn là "chưa đủ dữ liệu".
/// </summary>
public sealed class GoalPlanTool(
    IFinancialDataSource source, ISavingGoalRepository goals, PreliminaryCapacityService preliminary, ISystemConfigProvider config) : IAgentTool
{
    private const int Alternatives = 2;
    private const int MonthsPerYear = 12;

    public ToolSpec Spec { get; } = new(
        "goal_plan",
        "Đánh giá một MỤC TIÊU tiết kiệm target_vnd trong months tháng: mỗi tháng cần góp bao nhiêu, có khả thi với thu chi thật của người dùng không, nếu không thì kéo dài bao nhiêu năm thì đủ và mỗi tháng góp bao nhiêu.",
        [
            new ToolArg("target_vnd", "int", true, 1_000, AgentLimits.MaxPriceVnd),
            new ToolArg("months", "int", true, 1, AgentLimits.MaxSearchMonths),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var target = args.GetProperty("target_vnd").GetInt64();
        var months = (int)args.GetProperty("months").GetInt64();
        var required = CeilDiv(target, months);

        var series = await source.GetMonthlySeriesAsync(context.UserId, AgentLimits.HistoryMonths, context.Now, ct);
        var profile = IntervalForecaster.BuildProfile(series.IncomeVnd, await source.GetIncomeSegmentOverrideAsync(context.UserId, ct));
        if (profile.MonthsOfHistory < AgentLimits.MinHistoryMonths)
        {
            return await PreliminaryAsync(context, target, months, required, series, ct);
        }

        var spend = series.SpendVnd.OrderBy(v => v).ToArray();
        var conservative = profile.Q10 - Statistics.Quantile(spend, 0.9);
        var baseCapacity = profile.Q50 - Statistics.Quantile(spend, 0.5);
        var verdict = required <= conservative ? "feasible" : required <= baseCapacity ? "tight" : "infeasible";

        var now = context.Now;
        var settings = (await config.GetAsync(ct)).Document.Agent;
        var t = settings.Texts;
        var completeness = Math.Min(1.0, profile.MonthsOfHistory / (double)AgentLimits.HistoryMonths);
        var evidence = new List<Evidence>
        {
            Money(now, "goal_target", target, EvidenceSource.UserEntered, Text(t, "goal_target", ("months", months), ("duration", Years(months, t)))),
            Money(now, "plan_required", required, EvidenceSource.Estimated, Text(t, "plan_required", ("months", months), ("duration", Years(months, t)))),
            Money(now, "capacity_base", baseCapacity, EvidenceSource.Estimated, Text(t, "capacity_base"), completeness),
            Money(now, "capacity_conservative", conservative, EvidenceSource.Estimated, Text(t, "capacity_conservative"), completeness),
        };

        var actions = new List<SuggestedActionDto>();
        if (verdict != "feasible")
        {
            // infeasible: đủ ở mức điển hình; tight: đủ cả khi thu thấp chi cao (vùng đệm). Không còn dư ra đồng nào thì kéo dài bao lâu cũng không góp nổi: không phương án.
            var capacity = verdict == "infeasible" ? baseCapacity : conservative;
            if (capacity > 0)
            {
                var longer = (int)Math.Min(AgentLimits.MaxSearchMonths, CeilDiv(CeilDiv(target, capacity), MonthsPerYear) * MonthsPerYear);
                await AddAlternativesAsync(context, target, months, [longer, longer + MonthsPerYear], evidence, actions, t, ct);
                await AddCutSuggestionAsync(context, target, capacity, evidence, actions, settings, ct);
            }
        }

        evidence.Add(Verdict(now, verdict, months, completeness));
        return ToolResult.Ok(evidence, actions);
    }

    /// <summary>Ước tính sơ khảo khi chưa đủ lịch sử: dựa vào thu nhập người dùng đã khai và chi tiêu của các tháng đã ghi (xem mô tả lớp).</summary>
    private async Task<ToolResult> PreliminaryAsync(ToolContext context, long target, int months, long required, MonthlySeries series, CancellationToken ct)
    {
        var estimate = await preliminary.GetAsync(context.UserId, series, ct);
        if (estimate is null)
        {
            return ToolResult.Insufficient("history_too_short");
        }

        var income = estimate.Income;
        var now = context.Now;
        var settings = (await config.GetAsync(ct)).Document.Agent;
        var t = settings.Texts;
        var spendMonths = series.SpendVnd.Where(v => v > 0).ToArray();
        var completeness = estimate.SpendMonths / (double)AgentLimits.HistoryMonths;
        var share = (required * 100 + income / 2) / income;

        var evidence = new List<Evidence>
        {
            Money(now, "goal_target", target, EvidenceSource.UserEntered, Text(t, "goal_target", ("months", months), ("duration", Years(months, t)))),
            Money(now, "plan_required", required, EvidenceSource.Estimated, Text(t, "plan_required_share", ("months", months), ("duration", Years(months, t)), ("share", share))),
            Money(now, "income_declared", income, EvidenceSource.UserEntered, Text(t, "income_declared")),
        };

        // Khả năng góp theo căn cứ ưu tiên của PreliminaryCapacityService: chi tiêu thật, cách quản lý đã chọn, rồi khảo sát ban đầu.
        var capacity = estimate.Capacity;
        string basis = string.Empty;
        switch (estimate.Basis)
        {
            case CapacityBasis.Spend:
                basis = Text(t, "basis_spend", ("count", estimate.SpendMonths));
                evidence.Add(Money(now, "spend_avg", estimate.AverageSpend, EvidenceSource.Estimated, Text(t, "spend_avg", ("count", estimate.SpendMonths)), completeness));
                evidence.Add(Money(now, "capacity_base", capacity!.Value, EvidenceSource.Estimated, Text(t, "capacity_from_spend"), completeness));
                break;
            case CapacityBasis.Method:
                basis = Text(t, "basis_method", ("method", estimate.MethodName!), ("percent", estimate.Percent));
                evidence.Add(Money(now, "capacity_base", capacity!.Value, EvidenceSource.Estimated, Text(t, "capacity_from_method", ("method", estimate.MethodName!), ("percent", estimate.Percent))));
                break;
            case CapacityBasis.SurveySpend:
                basis = Text(t, "basis_survey_spend");
                evidence.Add(Money(now, "spend_declared", estimate.AverageSpend, EvidenceSource.UserEntered, Text(t, "spend_declared")));
                evidence.Add(Money(now, "capacity_base", capacity!.Value, EvidenceSource.Estimated, Text(t, "capacity_from_survey_spend")));
                break;
            case CapacityBasis.Survey:
                basis = Text(t, "basis_survey", ("percent", estimate.Percent));
                evidence.Add(Money(now, "capacity_base", capacity!.Value, EvidenceSource.Estimated, Text(t, "capacity_from_survey", ("percent", estimate.Percent))));
                break;
        }

        var actions = new List<SuggestedActionDto>();
        string? verdict = capacity is { } c ? (required <= c ? "tight" : "infeasible") : null; // dữ liệu còn mỏng: không bao giờ "feasible"
        if (verdict != "tight")
        {
            // Biết khả năng góp: kéo dài đến số năm tròn đủ góp. Chưa biết: chỉ thêm một và hai năm vào hạn người dùng nêu, không nói là đủ.
            int[] horizons;
            long cutCapacity = 0;
            if (capacity is > 0)
            {
                var longer = (int)Math.Min(AgentLimits.MaxSearchMonths, CeilDiv(CeilDiv(target, capacity.Value), MonthsPerYear) * MonthsPerYear);
                horizons = [longer, longer + MonthsPerYear];
                if (estimate.Basis == CapacityBasis.Spend)
                {
                    cutCapacity = capacity.Value;
                }
            }
            else if (capacity is null)
            {
                horizons = [months + MonthsPerYear, months + (2 * MonthsPerYear)];
            }
            else
            {
                horizons = []; // không còn dư ra đồng nào: kéo dài bao lâu cũng không góp nổi
            }

            await AddAlternativesAsync(context, target, months, horizons, evidence, actions, t, ct);
            if (cutCapacity > 0)
            {
                await AddCutSuggestionAsync(context, target, cutCapacity, evidence, actions, settings, ct);
            }
        }

        evidence.Add(new Evidence(
            "prelim_note", PreliminaryEvidenceKey, null,
            Text(t, "preliminary_note", ("basis", basis), ("minMonths", AgentLimits.MinHistoryMonths)),
            "label", EvidenceSource.Estimated, now, ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, Math.Max(completeness, 0.2))));
        if (verdict is not null)
        {
            evidence.Add(Verdict(now, verdict, months, Math.Max(completeness, 0.2)));
        }

        return ToolResult.Ok(evidence, actions);
    }

    /// <summary>Khóa của bằng chứng ghi chú "ước tính sơ bộ": app hiện nó thành dòng ghi chú cố định dưới thẻ trả lời (không phụ thuộc LLM có nhắc hay không).</summary>
    public const string PreliminaryEvidenceKey = "goal_plan_preliminary";

    /// <summary>
    /// Gợi ý theo người dùng: giảm <c>agent.cutSuggestionPercent</c>% chi của nhóm chi nhiều nhất (trung bình mỗi tháng trong <see cref="AgentLimits.MinHistoryMonths"/> tháng gần nhất) thì khả năng góp tăng bấy nhiêu,
    /// đủ mục tiêu sau bao lâu (làm tròn lên số năm tròn) và mỗi tháng góp bao nhiêu; kèm thẻ đặt hạn mức mới cho nhóm đó. Không có chi tiêu theo nhóm hoặc tắt (0) thì bỏ qua.
    /// </summary>
    private async Task AddCutSuggestionAsync(
        ToolContext context, long target, long capacity, List<Evidence> evidence, List<SuggestedActionDto> actions, AgentSettings settings, CancellationToken ct)
    {
        var percent = settings.CutSuggestionPercent;
        if (percent <= 0)
        {
            return;
        }

        var today = VietnamTime.DateOf(context.Now);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - AgentLimits.MinHistoryMonths);
        var top = (await source.GetCategorySpendAsync(context.UserId, from, today, ct))
            .Where(r => r.SpentVnd > 0 && r.Slug.Length > 0).OrderByDescending(r => r.SpentVnd).ThenBy(r => r.Slug, StringComparer.Ordinal).FirstOrDefault();
        if (top is null)
        {
            return;
        }

        var average = top.SpentVnd / AgentLimits.MinHistoryMonths;
        var cut = average * percent / 100;
        if (cut <= 0)
        {
            return;
        }

        var t = settings.Texts;
        var n = (int)Math.Min(AgentLimits.MaxSearchMonths, CeilDiv(CeilDiv(target, capacity + cut), MonthsPerYear) * MonthsPerYear);
        var monthly = CeilDiv(target, n);
        var limit = (average - cut) / 1_000 * 1_000;
        evidence.Add(Money(context.Now, "cut_saving", cut, EvidenceSource.Estimated, Text(t, "cut_saving", ("percent", percent), ("category", top.Name))));
        evidence.Add(Money(context.Now, "cut_horizon_monthly", monthly, EvidenceSource.Estimated, Text(t, "cut_horizon", ("percent", percent), ("category", top.Name), ("duration", Years(n, t)))));
        if (limit > 0)
        {
            // số trên nút "Đặt hạn mức" cũng phải có trong "Vì sao?": mọi số tiền trên thẻ đều có bằng chứng
            evidence.Add(Money(context.Now, "cut_limit", limit, EvidenceSource.Estimated, Text(t, "cut_limit", ("category", top.Name), ("percent", percent))));
            actions.Add(new SuggestedActionDto(
                SuggestedActionKinds.SetBudgetLimit,
                Text(t, "action_cut_limit", ("category", top.Name), ("limit", LocalTemplateComposer.FormatVnd(limit)), ("duration", Years(n, t))),
                limit, null, top.Slug, top.Name, ["cut_limit", "cut_saving", "cut_horizon_monthly"]));
        }
    }

    /// <summary>Thêm tối đa <see cref="Alternatives"/> phương án kéo dài: bằng chứng số cần góp mỗi tháng và thẻ đổi hạn (đã có mục tiêu cùng số tiền) hoặc tạo mục tiêu (chưa có).</summary>
    private async Task AddAlternativesAsync(
        ToolContext context, long target, int months, IReadOnlyList<int> horizons, List<Evidence> evidence, List<SuggestedActionDto> actions, IReadOnlyDictionary<string, string> t, CancellationToken ct)
    {
        var existing = (await goals.GetListForUserAsync(context.UserId, SavingGoalStatus.Active, ct))
            .Where(g => g.TargetCents == target).OrderByDescending(g => g.CreatedAt).FirstOrDefault();
        var today = VietnamTime.DateOf(context.Now);
        foreach (var n in horizons.Take(Alternatives))
        {
            if (n <= months || n > AgentLimits.MaxSearchMonths)
            {
                continue;
            }

            var monthly = CeilDiv(target, n);
            var id = $"alt_{actions.Count + 1}_monthly";
            evidence.Add(Money(context.Now, id, monthly, EvidenceSource.Estimated, Text(t, "alt_monthly", ("months", n), ("duration", Years(n, t)))));
            var deadline = today.AddMonths(n).ToString("yyyy-MM-dd");
            // Tiêu đề ngắn, tự đủ: app hiện nguyên văn (không ghép thêm số tiền hay ngày)
            actions.Add(existing is null
                ? new SuggestedActionDto(SuggestedActionKinds.CreateGoal, Text(t, "action_create_goal", ("duration", Years(n, t)), ("monthly", LocalTemplateComposer.FormatVnd(monthly))), target, deadline, null, null, [id])
                : new SuggestedActionDto(SuggestedActionKinds.ChangeGoalDeadline, Text(t, "action_change_deadline", ("duration", Years(n, t)), ("monthly", LocalTemplateComposer.FormatVnd(monthly))), monthly, deadline, null, null, [id], existing.Id.ToString()));
        }
    }

    private static Evidence Money(DateTimeOffset now, string id, long value, EvidenceSource source, string text, double fullness = 1.0)
        => new(id, id, value, text, "VND", source, now, ConfidenceCalculator.Compute(source, now, now, fullness));

    private static Evidence Verdict(DateTimeOffset now, string verdict, int months, double completeness)
        => new("verdict", "goal_plan_verdict", null, $"verdict: {verdict} (mục tiêu trong {months} tháng)", "label", EvidenceSource.Estimated, now,
            ConfidenceCalculator.Compute(EvidenceSource.Estimated, now, now, completeness));

    private static long CeilDiv(long number, long divisor) => (number + divisor - 1) / divisor;

    private static string Text(IReadOnlyDictionary<string, string> t, string key, params (string Slot, object Value)[] values) => AgentTexts.Render(t, key, values);

    /// <summary>"3 năm" khi tròn năm, ngược lại "3 năm 6 tháng" / "6 tháng" (câu chữ lấy từ mẫu <c>duration_*</c>).</summary>
    internal static string Years(int months, IReadOnlyDictionary<string, string>? t = null)
        => months % MonthsPerYear == 0 ? AgentTexts.Render(t, "duration_years", ("years", months / MonthsPerYear))
            : months < MonthsPerYear ? AgentTexts.Render(t, "duration_months", ("months", months))
            : AgentTexts.Render(t, "duration_years_months", ("years", months / MonthsPerYear), ("months", months % MonthsPerYear));
}
