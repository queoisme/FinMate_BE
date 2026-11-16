using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Công cụ tra tri thức: chỉ trả đoạn đã duyệt có nguồn, không có thì "thiếu dữ liệu" (không bịa), và câu chưa trả lời được thì được ghi nhận — chỉ qua mã băm phía ai-service.</summary>
public class SearchKnowledgeToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));
    private static JsonElement Args(string query) => JsonDocument.Parse(JsonSerializer.Serialize(new { query })).RootElement.Clone();

    private static Evidence Doc(string id = "kb:1") => new(id, "knowledge", null, "Quy tắc 50/30/20 chia thu nhập.", "text", EvidenceSource.Document, Now.AddDays(-5), 1.0, "Sổ tay FinMate");

    private sealed class FakeAi : IAgentAiClient
    {
        public List<Evidence> Found { get; set; } = [];
        public Exception? SearchError { get; set; }
        public List<(string Query, int K)> Searches { get; } = [];
        public List<string> Unanswered { get; } = [];
        public Func<AgentPlanRequest, AgentPlanResult> Plan { get; set; } = _ => new AgentPlanResult("knowledge", [new AgentPlanStep("search_knowledge", Args("50/30/20"))], null, null);
        public Exception? RecordError { get; set; }

        public Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default) => Task.FromResult(new AgentGateResult(true, "finance_cue", null));

        public Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default) => Task.FromResult(Plan(request));

        public Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default)
            => Task.FromResult(new AgentComposeResult([new AgentClaim("Theo tài liệu, quy tắc 50/30/20 chia thu nhập.", evidence.Select(e => e.Id).ToList())], false, false, null));

        public Task<IReadOnlyList<Evidence>> SearchKnowledgeAsync(string query, int k, CancellationToken ct = default)
        {
            Searches.Add((query, k));
            return SearchError is null ? Task.FromResult<IReadOnlyList<Evidence>>(Found) : Task.FromException<IReadOnlyList<Evidence>>(SearchError);
        }

        public Task RecordUnansweredAsync(string question, CancellationToken ct = default)
        {
            if (RecordError is not null)
            {
                return Task.FromException(RecordError);
            }

            Unanswered.Add(question);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AgentMemoryEntry>>([]);

        public Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (AgentOrchestrator Orchestrator, FakeAi Ai, AgentRunOptions Options) Build()
    {
        var ai = new FakeAi();
        var options = new AgentRunOptions();
        var source = new InMemoryFinancialDataSource([20_000_000], [10_000_000], 0);
        var registry = new ToolRegistry([new SearchKnowledgeAgentTool(ai, options)]);
        return (new AgentOrchestrator(ai, new ToolExecutor(registry), new IncomeHistory(source), new ConversationStore(new NoCache()), new AgentTraceContext(), () => Now, options), ai, options);
    }

    [Fact]
    public async Task approved_passages_come_back_as_document_evidence_with_their_source()
    {
        var (orchestrator, ai, _) = Build();
        ai.Found = [Doc()];

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "Quy tắc 50/30/20 là gì?");

        answer.Status.Should().Be(AgentStatus.Answered);
        answer.Evidence.Should().ContainSingle().Which.Should().Match<AgentEvidenceDto>(e => e.Source == "document" && e.SourceName == "Sổ tay FinMate" && e.ValueVnd == null);
        ai.Searches.Should().Equal(("50/30/20", 3));
        ai.Unanswered.Should().BeEmpty();
    }

    [Fact]
    public async Task no_approved_passage_means_insufficient_data_and_the_question_is_recorded_as_unanswered()
    {
        var (orchestrator, ai, _) = Build();

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "Quỹ hưu trí tự nguyện hoạt động thế nào?");

        answer.Status.Should().Be(AgentStatus.InsufficientData);
        answer.Message.Should().Be(AgentMessages.Insufficient("knowledge_missing"));
        answer.Claims.Should().BeEmpty();
        ai.Unanswered.Should().Equal("Quỹ hưu trí tự nguyện hoạt động thế nào?");
    }

    [Fact]
    public async Task a_question_with_no_plan_step_is_recorded_too()
    {
        var (orchestrator, ai, _) = Build();
        ai.Plan = _ => new AgentPlanResult("unknown", [], null, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "câu hỏi chưa ai lập kế hoạch");

        answer.Status.Should().Be(AgentStatus.Clarify);
        ai.Unanswered.Should().HaveCount(1);
    }

    [Fact]
    public async Task when_the_ai_service_is_down_the_tool_says_insufficient_instead_of_failing_the_turn()
    {
        var (orchestrator, ai, _) = Build();
        ai.SearchError = new AIServiceUnavailableException("sập");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "Quy tắc 50/30/20 là gì?");

        answer.Status.Should().Be(AgentStatus.InsufficientData);
    }

    [Fact]
    public async Task a_failing_unanswered_report_never_breaks_the_answer()
    {
        var (orchestrator, ai, _) = Build();
        ai.RecordError = new AIServiceUnavailableException("sập");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "câu hỏi lạ");

        answer.Status.Should().Be(AgentStatus.InsufficientData);
    }

    [Fact]
    public async Task rag_off_returns_nothing_without_calling_the_store()
    {
        var (orchestrator, ai, options) = Build();
        ai.Found = [Doc()];
        options.RagEnabled = false;

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "Quy tắc 50/30/20 là gì?");

        answer.Status.Should().Be(AgentStatus.InsufficientData);
        ai.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task a_query_longer_than_the_limit_is_rejected_before_the_store_is_touched()
    {
        var (orchestrator, ai, _) = Build();
        ai.Plan = _ => new AgentPlanResult("knowledge", [new AgentPlanStep("search_knowledge", Args(new string('a', 121)))], null, null);

        await orchestrator.AskAsync(Guid.NewGuid(), "x");

        ai.Searches.Should().BeEmpty();
    }

    [Fact]
    public void the_tool_spec_exposes_one_bounded_string_argument()
    {
        var spec = new SearchKnowledgeAgentTool(Mock.Of<IAgentAiClient>(), new AgentRunOptions()).Spec;

        spec.Name.Should().Be("search_knowledge");
        spec.Args.Should().ContainSingle().Which.Should().Be(new ToolArg("query", "string", true, Max: 120));
    }
}
