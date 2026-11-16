using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Gọi <c>/api/v1/agent/{gate,plan,compose}</c> của ai-service. Khóa nội bộ và timeout nằm ở HttpClient đã cấu hình ở DI. Không lời gọi nào mang <c>userId</c> hay mã băm của nó,
/// và KHÔNG ghi log câu hỏi, bằng chứng hay câu trả lời — chỉ mã trạng thái cố định (cùng ràng buộc riêng tư với <see cref="MethodAiClient"/>).
/// Mọi lỗi (mạng, hết giờ, HTTP khác 2xx, thân không đọc được) đều thành <see cref="AIServiceUnavailableException"/>.
/// </summary>
public class AgentAiClient(HttpClient http, ILogger<AgentAiClient> logger, AgentTraceContext trace) : IAgentAiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record GateRequest(string Text);

    private sealed record GateResponse(bool InScope, string ReasonCode, string? RefusalText);

    private sealed record WireTool(string Name, string Description, Dictionary<string, WireArg> ArgsSchema);

    private sealed record WireArg(string Type, bool Required, long? Min, long? Max);

    private sealed record WireFeedback(string Tool, string Code);

    private sealed record WireState(string? LastIntent, Dictionary<string, string> Slots);

    private sealed record WireMemoryItem(string Key, string Value);

    private sealed record PlanRequestBody(string Text, string Segment, List<WireTool> Tools, int MaxSteps, List<WireFeedback>? Feedback, WireState? State, List<WireMemoryItem>? Memory);

    private sealed record WireMemoryEntry(string Key, string Value, DateTimeOffset UpdatedAt);

    private sealed record MemoryPutBody(string UserIdHash, string Value);

    private sealed record WireDeleted(int Deleted);

    private sealed record WireTicketMessage(string Role, string Text);

    private sealed record TicketAssistBody(string Subject, string Body, List<WireTicketMessage> History);

    private sealed record TicketAssistResponseBody(string Summary, string? SuggestedReply, bool Verified, WireUsage? Usage);

    private sealed record KbSearchBody(string Query, int K);

    private sealed record KbSearchResponse(List<WireEvidence> Evidence);

    private sealed record KbCandidateBody(string Text);

    private sealed record WireRecorded(bool Recorded);

    private sealed record WireStep(string Tool, JsonElement Args);

    private sealed record WireUsage(int InputTokens, int OutputTokens, string Provider, string Model);

    private sealed record PlanResponseBody(string Intent, List<WireStep>? Steps, string? Clarify, WireUsage? Usage);

    private sealed record WireEvidence(string Id, string Key, long? ValueVnd, string? Text, string Unit, string Source, DateTimeOffset AsOf, double Confidence, string? SourceName, string? SourceUrl);

    private sealed record ComposeRequestBody(string Question, List<WireEvidence> Evidence, bool SkipVerifier);

    private sealed record WireClaim(string Text, List<string>? EvidenceIds);

    private sealed record ComposeResponseBody(List<WireClaim>? Claims, bool UsedTemplate, bool NoData, WireUsage? Usage);

    public async Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default)
    {
        var response = await PostAsync<GateRequest, GateResponse>("api/v1/agent/gate", new GateRequest(text), "gate", ct);
        return new AgentGateResult(response.InScope, response.ReasonCode, response.RefusalText);
    }

    public async Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default)
    {
        var body = new PlanRequestBody(
            request.Text,
            request.Segment,
            request.Tools.Select(t => new WireTool(t.Name, t.Description, t.Args.ToDictionary(a => a.Name, a => new WireArg(a.Type, a.Required, a.Type == "int" ? a.Min : null, a.Type == "int" ? a.Max : null)))).ToList(),
            request.MaxSteps,
            request.Feedback?.Select(f => new WireFeedback(f.Tool, f.Code)).ToList(),
            request.State is null ? null : new WireState(request.State.LastIntent, request.State.Slots.ToDictionary(s => s.Key, s => s.Value)),
            request.Memory?.Select(m => new WireMemoryItem(m.Key, m.Value)).ToList());

        var response = await PostAsync<PlanRequestBody, PlanResponseBody>("api/v1/agent/plan", body, "plan", ct);
        return new AgentPlanResult(
            response.Intent,
            (response.Steps ?? []).Select(s => new AgentPlanStep(s.Tool, s.Args.Clone())).ToList(),
            string.IsNullOrWhiteSpace(response.Clarify) ? null : response.Clarify,
            Usage(response.Usage));
    }

    public async Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default)
    {
        var body = new ComposeRequestBody(
            question,
            evidence.Select(e => new WireEvidence(e.Id, e.Key, e.ValueVnd, e.Text, e.Unit, AgentEvidenceDto.WireSource(e.Source), e.AsOf, e.Confidence, e.SourceName, e.SourceUrl)).ToList(),
            skipVerifier);

        var response = await PostAsync<ComposeRequestBody, ComposeResponseBody>("api/v1/agent/compose", body, "compose", ct);
        return new AgentComposeResult(
            (response.Claims ?? []).Select(c => new AgentClaim(c.Text, c.EvidenceIds ?? [])).ToList(),
            response.UsedTemplate,
            response.NoData,
            Usage(response.Usage));
    }

    public async Task<IReadOnlyList<Evidence>> SearchKnowledgeAsync(string query, int k, CancellationToken ct = default)
    {
        var response = await PostAsync<KbSearchBody, KbSearchResponse>("api/v1/kb/search", new KbSearchBody(query, k), "kb_search", ct);
        return response.Evidence.Select(e => new Evidence(e.Id, e.Key, e.ValueVnd, e.Text, e.Unit, AgentEvidenceDto.ParseSource(e.Source), e.AsOf, e.Confidence, e.SourceName, e.SourceUrl)).ToList();
    }

    public async Task<TicketAssistResult?> TicketAssistAsync(string subject, string body, IReadOnlyList<TicketAssistMessage> history, CancellationToken ct = default)
    {
        var response = await PostAsync<TicketAssistBody, TicketAssistResponseBody>("api/v1/agent/ticket-assist", new TicketAssistBody(subject, body, history.Select(h => new WireTicketMessage(h.Role, h.Text)).ToList()), "ticket_assist", ct);
        return new TicketAssistResult(response.Summary, response.SuggestedReply, response.Verified, Usage(response.Usage));
    }

    public async Task RecordUnansweredAsync(string question, CancellationToken ct = default)
        => await PostAsync<KbCandidateBody, WireRecorded>("api/v1/kb/candidates", new KbCandidateBody(question), "kb_candidate", ct);

    public async Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default)
    {
        var entries = await SendAsync<List<WireMemoryEntry>>(HttpMethod.Get, $"api/v1/agent/memory?user_id_hash={UserIdHasher.Hash(userId)}", null, "memory_list", ct);
        return entries.Select(e => new AgentMemoryEntry(e.Key, e.Value, e.UpdatedAt)).ToList();
    }

    public async Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default)
        => await SendAsync<WireMemoryEntry>(HttpMethod.Put, $"api/v1/agent/memory/{Uri.EscapeDataString(key)}", new MemoryPutBody(UserIdHasher.Hash(userId), value), "memory_put", ct);

    public async Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default)
    {
        var path = key is null ? "api/v1/agent/memory" : $"api/v1/agent/memory/{Uri.EscapeDataString(key)}";
        return (await SendAsync<WireDeleted>(HttpMethod.Delete, $"{path}?user_id_hash={UserIdHasher.Hash(userId)}", null, "memory_delete", ct)).Deleted;
    }

    private static AgentUsage? Usage(WireUsage? usage) => usage is null ? null : new AgentUsage(usage.InputTokens, usage.OutputTokens, usage.Provider, usage.Model);

    private Task<TResponse> PostAsync<TRequest, TResponse>(string route, TRequest body, string step, CancellationToken ct)
        => SendAsync<TResponse>(HttpMethod.Post, route, body, step, ct);

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string route, object? body, string step, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(method, route);
            if (trace.IsActive)
            {
                message.Headers.Add("X-Trace-Id", trace.TraceId.ToString());
                message.Headers.Add("X-User-Hash", UserIdHasher.Hash(trace.UserId));
            }

            if (body is not null)
            {
                message.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            using var response = await http.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Agent {Step} via AI Service failed: HTTP {Status}", step, (int)response.StatusCode);
                throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
            }

            return await response.Content.ReadFromJsonAsync<TResponse>(Json, ct)
                   ?? throw new AIServiceUnavailableException("AI Service trả phản hồi rỗng.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning("Agent {Step} via AI Service unavailable: {Reason}", step, ex.GetType().Name);
            throw new AIServiceUnavailableException("AI Service không phản hồi hoặc trả lỗi.");
        }
    }
}
