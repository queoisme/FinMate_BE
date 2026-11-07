using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

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
public sealed class AgentOrchestrator(IAgentAiClient ai, ToolExecutor tools, IncomeHistory income, Func<DateTimeOffset>? now = null) : IAgentOrchestrator
{
    public async Task<AgentAnswerDto> AskAsync(Guid userId, string question, CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid();
        var clock = (now ?? (() => DateTimeOffset.UtcNow))();
        var context = new ToolContext(userId, clock);

        var gate = await ai.GateAsync(question, ct);
        if (!gate.InScope)
        {
            return Reply(AgentStatus.Refused, gate.RefusalText ?? AgentMessages.OutOfScope, traceId);
        }

        var profile = await income.GetAsync(userId, clock, ct);
        var request = new AgentPlanRequest(question, profile.Segment.ToString().ToLowerInvariant(), tools.Registry.Specs, AgentLimits.MaxPlanSteps);
        var plan = await ai.PlanAsync(request, ct);

        var evidence = new List<Evidence>();
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
                switch (result.Status)
                {
                    case ToolStatus.Ok:
                        evidence.AddRange(result.Evidence.Where(e => evidence.All(x => x.Id != e.Id)));
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
        }

        if (evidence.Count == 0)
        {
            return insufficient is not null
                ? Reply(AgentStatus.InsufficientData, AgentMessages.Insufficient(insufficient), traceId)
                : Reply(AgentStatus.Clarify, AgentMessages.NotUnderstood, traceId);
        }

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
            composed = await ai.ComposeAsync(question, evidence, skipVerifier: false, ct);
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
