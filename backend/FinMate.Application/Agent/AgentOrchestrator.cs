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
}

/// <summary>Các câu cố định của vòng agent (một chỗ duy nhất; câu từ chối theo chủ đề do ai-service lấy từ DB).</summary>
public static class AgentMessages
{
    public const string OutOfScope = "Mình chỉ hỗ trợ các câu hỏi về tài chính cá nhân của bạn.";
    public const string NotUnderstood = "Mình chưa hiểu bạn muốn xem điều gì. Bạn nói rõ hơn giúp mình nhé, ví dụ: \"Mua xe 600 triệu sau 2 năm được không?\".";

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
public sealed class AgentOrchestrator(IAgentAiClient ai, ToolExecutor tools, IncomeHistory income, ConversationStore conversations, AgentTraceContext trace, Func<DateTimeOffset>? now = null, AgentRunOptions? options = null) : IAgentOrchestrator
{
    public async Task<AgentAnswerDto> AskAsync(Guid userId, string question, CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid();
        trace.Begin(traceId, userId); // từ đây mọi lời gọi sang ai-service mang X-Trace-Id (và mã băm của người dùng) để vết của lượt này nối lại được
        var clock = (now ?? (() => DateTimeOffset.UtcNow))();
        var context = new ToolContext(userId, clock);

        var gate = await ai.GateAsync(question, ct);
        if (!gate.InScope)
        {
            return Reply(AgentStatus.Refused, gate.RefusalText ?? AgentMessages.OutOfScope, traceId);
        }

        var profile = await income.GetAsync(userId, clock, ct);
        var stored = await TryAsync(() => conversations.GetAsync(userId, ct));
        var state = stored is not null && stored.Turn < AgentLimits.MaxTurns ? stored : null; // đủ MaxTurns lượt thì ngữ cảnh bắt đầu lại
        var request = new AgentPlanRequest(
            question, profile.Segment.ToString().ToLowerInvariant(), tools.Registry.Specs, AgentLimits.MaxPlanSteps,
            State: state is null ? null : new AgentPlanState(state.LastIntent, state.Slots),
            Memory: await ReadMemoryAsync(userId, ct));
        var plan = await ai.PlanAsync(request, ct);
        trace.AddUsage(plan.Usage);

        var evidence = new List<Evidence>();
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

        var (claims, usedTemplate) = await ComposeAsync(question, evidence, ct);
        return new AgentAnswerDto(
            AgentStatus.Answered,
            null,
            claims,
            evidence.Select(AgentEvidenceDto.From).ToList(),
            VerdictOf(evidence),
            traceId,
            usedTemplate);
    }

    private async Task<(IReadOnlyList<AgentClaimDto> Claims, bool UsedTemplate)> ComposeAsync(string question, List<Evidence> evidence, CancellationToken ct)
    {
        AgentComposeResult? composed = null;
        try
        {
            composed = await ai.ComposeAsync(question, evidence, skipVerifier: options?.SkipVerifier ?? false, ct);
            trace.AddUsage(composed.Usage);
        }
        catch (AIServiceUnavailableException)
        {
            // ai-service sập giữa vòng: bằng chứng đã có trong tay nên vẫn trả lời được bằng mẫu, không 500
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

    /// <summary>Nhớ giá và thời hạn của lần dự báo thành công để câu nối tiếp ("còn 3 năm thì sao?") dùng lại giá cũ. Chỉ số nguyên đã qua kiểm tham số.</summary>
    private static void Remember(Dictionary<string, string> slots, AgentPlanStep step)
    {
        if (step.Tool != "forecast_goal")
        {
            return;
        }

        foreach (var name in new[] { "price_vnd", "horizon_months" })
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
        => new(status, message, [], [], null, traceId, false);
}
