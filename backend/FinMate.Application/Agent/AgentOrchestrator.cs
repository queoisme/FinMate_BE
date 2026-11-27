using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>Cắt bỏ từng thành phần khi đánh giá (đăng ký theo request; chỉ <c>AgentEvalController</c> đặt giá trị). Mặc định là chạy đủ.</summary>
public sealed class AgentRunOptions
{
    public bool SkipVerifier { get; set; }

    /// <summary>false = công cụ tra tri thức không trả gì (đánh giá cắt bỏ RAG).</summary>
    public bool RagEnabled { get; set; } = true;
}

public interface IAgentOrchestrator
{
    Task<AgentAnswerDto> AskAsync(Guid userId, string question, CancellationToken ct = default);

    /// <summary>Đánh giá mục tiêu tiết kiệm khi app đã biết số tiền và số tháng: chạy thẳng công cụ <c>goal_plan</c>, không nhờ LLM chọn công cụ (nên không phụ thuộc LLM ở bước đầu).</summary>
    Task<AgentAnswerDto> AssessGoalAsync(Guid userId, long targetVnd, int months, CancellationToken ct = default);
}

/// <summary>Các câu cố định của vòng agent (một chỗ duy nhất; câu từ chối theo chủ đề do ai-service lấy từ DB).</summary>
public static class AgentMessages
{
    public const string OutOfScope = "Mình chỉ hỗ trợ các câu hỏi về tài chính cá nhân của bạn.";
    public const string NotUnderstood = "Mình chưa hiểu bạn muốn xem điều gì. Bạn nói rõ hơn giúp mình nhé, ví dụ: \"Mua xe 600 triệu sau 2 năm được không?\".";
    public const string SmallTalk = "Mình đây! Bạn muốn xem điều gì về tài chính của mình? Ví dụ: \"Tháng này tôi tiêu bao nhiêu?\" hoặc \"Mua xe 600 triệu sau 2 năm được không?\".";

    public static string Insufficient(string? code) => code switch
    {
        "history_too_short" => $"Mình cần ít nhất {AgentLimits.MinHistoryMonths} tháng lịch sử thu nhập để dự báo. Bạn ghi chép thêm giao dịch rồi hỏi lại nhé.",
        "assumption_missing" => "Mình chưa có giả định kinh tế đã duyệt cho nội dung này.",
        "goal_exceeds_income" => "Số tiền muốn tiết kiệm bằng hoặc lớn hơn thu nhập nên không còn gì để chia cho chi tiêu. Bạn thử mức tiết kiệm thấp hơn nhé.",
        "no_budget_basis" => "Mình chưa có chi tiêu 3 tháng gần nhất của bạn hay mẫu ngân sách nào để chia. Bạn ghi chép thêm giao dịch rồi hỏi lại nhé.",
        "knowledge_missing" => "Mình chưa có tài liệu đã được duyệt về nội dung này nên không muốn trả lời theo cảm tính.",
        _ => "Mình chưa đủ dữ liệu để trả lời câu hỏi này.",
    };
}

/// <summary>
/// Vòng agent (cách B): GATE → PLAN → EXECUTE → COMPOSE → trả. Backend giữ <c>userId</c> (từ JWT) và là nơi DUY NHẤT chạm dữ liệu thật; ai-service chỉ thấy câu hỏi và bằng chứng đã đóng gói.
/// <list type="bullet">
/// <item>Công cụ chỉ chạy nếu nằm trong <see cref="ToolRegistry.Allowlist"/> và qua kiểm tham số; bị từ chối thì lập lại kế hoạch tối đa <see cref="AgentLimits.MaxReplans"/> lần kèm lý do.</item>
/// <item>Thiếu dữ liệu (<c>InsufficientData</c>) thì dừng: lập lại kế hoạch không tạo ra dữ liệu. Không bao giờ bịa.</item>
/// <item>Mệnh đề trích dẫn mã bằng chứng KHÔNG thuộc lượt này bị bỏ mã đó (không rò bằng chứng của lượt/người khác); mất hết mã thì mệnh đề bị bỏ.</item>
/// <item>ai-service sập sau khi đã có bằng chứng thì trả câu mẫu dựng từ bằng chứng, không 500; sập trước khi có bằng chứng thì 503.</item>
/// </list>
/// </summary>
public sealed class AgentOrchestrator(
    IAgentAiClient ai, ToolExecutor tools, IncomeHistory income, ConversationStore conversations, AgentTraceContext trace, Func<DateTimeOffset>? now = null,
    AgentRunOptions? options = null, ListenerHealth.IDataCoverageService? coverage = null, ISystemConfigProvider? config = null) : IAgentOrchestrator
{
    /// <summary>Mã cổng phạm vi khi câu không có từ khóa tài chính nào (khác với chủ đề bị từ chối riêng).</summary>
    public const string GateReasonNoCue = "out_of_scope";

    /// <summary>Mã cổng khi câu chỉ là xã giao ("ừ", "xin chào", "cảm ơn"): trong phạm vi nhưng không có gì để lập kế hoạch.</summary>
    public const string GateReasonSmallTalk = "smalltalk";

    /// <summary>Bằng chứng độ phủ dữ liệu (Phase 84, BR-LH-09).</summary>
    public const string DataCoverageEvidenceId = "data_coverage";

    /// <summary>Công cụ không đọc giao dịch của user: trả lời bằng chúng không cần nhắc dữ liệu có thể thiếu.</summary>
    private static readonly HashSet<string> NonUserDataTools = ["search_knowledge", "get_assumptions"];

    public async Task<AgentAnswerDto> AskAsync(Guid userId, string question, CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid();
        trace.Begin(traceId, userId); // từ đây mọi lời gọi sang ai-service mang X-Trace-Id (và mã băm của người dùng) để vết của lượt này nối lại được
        var clock = (now ?? (() => DateTimeOffset.UtcNow))();
        var context = new ToolContext(userId, clock);

        var stored = await TryAsync(() => conversations.GetAsync(userId, ct));
        var gate = await ai.GateAsync(question, ct);
        // Câu nối tiếp ("còn nếu 5 năm thì sao?") không có từ khóa tài chính nên cổng luật coi là ngoài phạm vi. Đang có cuộc hội thoại tài chính thì để bước lập kế hoạch quyết định
        // (nó có ngữ cảnh, và chỉ được chọn công cụ trong danh sách); chủ đề bị từ chối riêng (vd tư vấn đầu tư) thì vẫn từ chối.
        var followUp = stored is not null && gate.ReasonCode == GateReasonNoCue;
        if (!gate.InScope && !followUp)
        {
            return Reply(AgentStatus.Refused, gate.RefusalText ?? AgentMessages.OutOfScope, traceId);
        }

        // Xã giao không có việc nào để làm. Cho qua bước lập kế hoạch thì LLM đọc nó như câu nối tiếp của lượt trước và chạy lại công cụ cũ ("ừ" sau một lần so sánh thì ra lại bản so sánh).
        if (gate.ReasonCode == GateReasonSmallTalk && gate.DirectTool is null)
        {
            return Reply(AgentStatus.Clarify, AgentMessages.SmallTalk, traceId);
        }

        var profile = await income.GetAsync(userId, clock, ct);
        var state = stored is not null && stored.Turn < AgentLimits.MaxTurns ? stored : null; // đủ MaxTurns lượt thì ngữ cảnh bắt đầu lại
        var request = new AgentPlanRequest(
            question, profile.Segment.ToString().ToLowerInvariant(), await tools.Registry.SpecsAsync(ct), AgentLimits.MaxPlanSteps,
            State: state is null ? null : new AgentPlanState(state.LastIntent, state.Slots),
            Memory: await ReadMemoryAsync(userId, ct));
        // Câu quen về dữ liệu quản lý: cổng đã chọn sẵn công cụ bằng luật, bỏ lượt LLM lập kế hoạch (công cụ vẫn qua kiểm danh sách cho phép và tham số như mọi kế hoạch).
        var plan = DirectPlan(gate) ?? await ai.PlanAsync(request, ct);
        trace.AddUsage(plan.Usage);

        var evidence = new List<Evidence>();
        var actions = new List<SuggestedActionDto>();
        var usedUserData = false;
        var slots = new Dictionary<string, string>(state?.Slots ?? []);
        string? insufficient = null;
        for (var replan = 0; ; replan++)
        {
            if (plan.Clarify is not null)
            {
                return Reply(AgentStatus.Clarify, plan.Clarify, traceId);
            }

            var rejected = new List<AgentPlanFeedback>();
            foreach (var step in plan.Steps.Take(AgentLimits.MaxPlanSteps))
            {
                var result = await tools.ExecuteAsync(context, step.Tool, step.Args, ct);
                trace.Steps.Add((step.Tool, result.Status.ToString()));
                switch (result.Status)
                {
                    case ToolStatus.Ok:
                        evidence.AddRange(result.Evidence.Where(e => evidence.All(x => x.Id != e.Id)));
                        actions.AddRange(result.Actions ?? []);
                        usedUserData |= !NonUserDataTools.Contains(step.Tool);
                        Remember(slots, step);
                        break;
                    case ToolStatus.InsufficientData:
                        insufficient ??= result.Code;
                        break;
                    default:
                        rejected.Add(new AgentPlanFeedback(step.Tool, result.Code ?? "rejected"));
                        break;
                }
            }

            if (insufficient is not null || rejected.Count == 0 || replan >= AgentLimits.MaxReplans)
            {
                break;
            }

            plan = await ai.PlanAsync(request with { Feedback = rejected }, ct);
            trace.AddUsage(plan.Usage);
        }

        if (evidence.Count == 0)
        {
            if (insufficient is null or "knowledge_missing")
            {
                await TryAsync(async () => { await ai.RecordUnansweredAsync(question, ct); return true; }); // chỉ mã băm được lưu: nhân sự nội dung thấy kho còn thiếu gì
            }

            return insufficient is not null
                ? Reply(AgentStatus.InsufficientData, AgentMessages.Insufficient(insufficient), traceId)
                : Reply(AgentStatus.Clarify, AgentMessages.NotUnderstood, traceId);
        }

        await TryAsync(async () =>
        {
            await conversations.SaveAsync(userId, new ConversationState(plan.Intent, slots, (state?.Turn ?? 0) + 1, clock), ct);
            return true;
        });

        return await ComposeAnswerAsync(userId, question, evidence, actions, usedUserData, traceId, clock, ct);
    }

    public async Task<AgentAnswerDto> AssessGoalAsync(Guid userId, long targetVnd, int months, CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid();
        trace.Begin(traceId, userId);
        var clock = (now ?? (() => DateTimeOffset.UtcNow))();
        var args = JsonSerializer.SerializeToElement(new { target_vnd = targetVnd, months });
        var result = await tools.ExecuteAsync(new ToolContext(userId, clock), "goal_plan", args, ct);
        switch (result.Status)
        {
            case ToolStatus.Ok:
                break;
            case ToolStatus.InsufficientData:
                return Reply(AgentStatus.InsufficientData, AgentMessages.Insufficient(result.Code), traceId);
            default:
                return Reply(AgentStatus.Clarify, AgentMessages.NotUnderstood, traceId);
        }

        var stored = await TryAsync(() => conversations.GetAsync(userId, ct));
        var slots = new Dictionary<string, string>();
        Remember(slots, new AgentPlanStep("goal_plan", args));
        await TryAsync(async () =>
        {
            await conversations.SaveAsync(userId, new ConversationState("goal_plan", slots, (stored?.Turn ?? 0) + 1, clock), ct);
            return true;
        });

        var texts = config is null ? null : (await config.GetAsync(ct)).Document.Agent.Texts;
        var question = AgentTexts.Render(texts, "goal_plan_question", ("target", targetVnd), ("months", months));
        return await ComposeAnswerAsync(userId, question, [.. result.Evidence], [.. result.Actions ?? []], true, traceId, clock, ct);
    }

    private async Task<AgentAnswerDto> ComposeAnswerAsync(
        Guid userId, string question, List<Evidence> evidence, List<SuggestedActionDto> actions, bool usedUserData, Guid traceId, DateTimeOffset clock, CancellationToken ct)
    {
        // BR-LH-09: câu trả lời dựa trên giao dịch của user mà độ phủ kém thì PHẢI nói dữ liệu có thể thiếu. Bằng chứng đưa cho LLM thấy, còn câu cố định
        // được ghép vào cuối (nếu chưa có mệnh đề nào trích nó) để LLM có lờ đi cũng không mất. Câu chỉ chứa phần trăm, không có số tiền, nên qua được bộ kiểm số (S5).
        var coverageWarning = usedUserData ? await PoorCoverageWarningAsync(userId, ct) : null;
        if (coverageWarning is not null)
        {
            evidence.Add(new Evidence(DataCoverageEvidenceId, DataCoverageEvidenceId, null, coverageWarning, "percent", EvidenceSource.Estimated, clock, 1.0));
        }

        var (claims, usedTemplate) = await ComposeAsync(question, evidence, ct);
        if (coverageWarning is not null && claims.All(c => !c.EvidenceIds.Contains(DataCoverageEvidenceId)))
        {
            claims = [.. claims, new AgentClaimDto(coverageWarning, [DataCoverageEvidenceId])];
        }

        return new AgentAnswerDto(
            AgentStatus.Answered,
            null,
            claims,
            evidence.Select(AgentEvidenceDto.From).ToList(),
            VerdictOf(evidence),
            traceId,
            usedTemplate,
            actions.DistinctBy(a => (a.Kind, a.Date, a.AmountVnd, a.CategorySlug)).Take(AgentLimits.MaxSuggestedActions).ToList());
    }

    private AgentPlanResult? DirectPlan(AgentGateResult gate)
    {
        if (gate.DirectTool is null)
        {
            return null;
        }

        var args = JsonSerializer.SerializeToElement(gate.DirectArgs ?? new Dictionary<string, long>());
        return new AgentPlanResult(gate.DirectTool, [new AgentPlanStep(gate.DirectTool, args)], null, null);
    }

    private async Task<(IReadOnlyList<AgentClaimDto> Claims, bool UsedTemplate)> ComposeAsync(string question, List<Evidence> evidence, CancellationToken ct)
    {
        AgentComposeResult? composed = null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(AgentLimits.ComposeTimeoutSeconds));
        try
        {
            composed = await ai.ComposeAsync(question, evidence, skipVerifier: options?.SkipVerifier ?? false, limit.Token);
            trace.AddUsage(composed.Usage);
        }
        catch (AIServiceUnavailableException)
        {
            // ai-service sập giữa vòng: bằng chứng đã có trong tay nên vẫn trả lời được bằng mẫu, không 500
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // quá hạn chờ LLM viết câu: dùng mẫu (người dùng bỏ đi thì ct bị hủy và lỗi vẫn nổi lên như cũ)
        }

        var known = evidence.Select(e => e.Id).ToHashSet();
        var claims = composed?.Claims
            .Select(c => new AgentClaimDto(c.Text, c.EvidenceIds.Where(known.Contains).ToList()))
            .Where(c => c.EvidenceIds.Count > 0 || c.Text.TrimEnd().EndsWith('?'))
            .ToList() ?? [];

        if (claims.Count > 0)
        {
            return (claims, composed!.UsedTemplate);
        }

        return (LocalTemplateComposer.Compose(evidence).Select(c => new AgentClaimDto(c.Text, c.EvidenceIds)).ToList(), true);
    }

    /// <summary>Câu cảnh báo khi độ phủ kém; null khi ổn, chưa biết, hoặc không đọc được độ phủ (trục trặc ở đây không được làm hỏng lượt hỏi).</summary>
    private async Task<string?> PoorCoverageWarningAsync(Guid userId, CancellationToken ct)
    {
        if (coverage is null)
        {
            return null;
        }

        var summary = await TryAsync(() => coverage.GetForecastSummaryAsync(userId, ct));
        return summary is { IsPoor: true } ? summary.Warning : null;
    }

    /// <summary>Ngữ cảnh và bộ nhớ chỉ giúp hiểu câu nối tiếp: Redis hay ai-service trục trặc ở đây không được làm hỏng lượt hỏi, chỉ mất phần tiện lợi.</summary>
    private static async Task<T?> TryAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default;
        }
    }

    private async Task<IReadOnlyList<AgentMemoryItem>?> ReadMemoryAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            return (await ai.GetMemoryAsync(userId, ct)).Select(m => new AgentMemoryItem(m.Key, m.Value)).ToList();
        }
        catch (AIServiceUnavailableException)
        {
            return null;
        }
    }

    /// <summary>Tham số nguyên của lượt thành công được nhớ cho câu nối tiếp ("còn 3 năm thì sao?", "vậy tháng trước?", "nếu tiết kiệm 5 triệu thì sao?"). Chỉ số nguyên đã qua kiểm tham số.</summary>
    private static readonly Dictionary<string, string[]> RememberedArgs = new()
    {
        ["forecast_goal"] = ["price_vnd", "horizon_months"],
        ["simulate_purchase"] = ["price_vnd"],
        ["spending_summary"] = ["months"],
        ["compare_spending"] = ["months"],
        ["plan_budget"] = ["income_vnd", "save_vnd"],
        ["propose_budget_limit"] = ["limit_vnd"],
        ["goal_plan"] = ["target_vnd", "months"],
    };

    private static void Remember(Dictionary<string, string> slots, AgentPlanStep step)
    {
        if (!RememberedArgs.TryGetValue(step.Tool, out var names))
        {
            return;
        }

        foreach (var name in names)
        {
            if (step.Args.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
            {
                slots[name] = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    private static string? VerdictOf(List<Evidence> evidence)
    {
        const string prefix = "verdict: ";
        var text = evidence.FirstOrDefault(e => e.Id == "verdict")?.Text;
        if (text is null || !text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = text[prefix.Length..];
        var end = rest.IndexOf(' ');
        return end < 0 ? rest : rest[..end];
    }

    private static AgentAnswerDto Reply(string status, string message, Guid traceId)
        => new(status, message, [], [], null, traceId, false, []);
}
