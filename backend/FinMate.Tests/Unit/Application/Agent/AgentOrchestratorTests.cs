using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Vòng Gate → Plan → Execute → Compose của agent. ai-service giả; công cụ và dữ liệu thật (trong bộ nhớ).</summary>
public class AgentOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly long[] Income = [25_000_000, 25_000_000, 25_000_000, 25_000_000, 25_000_000, 25_000_000];
    private static readonly long[] Spend = [15_000_000, 15_000_000, 15_000_000, 15_000_000, 15_000_000, 15_000_000];
    private static readonly JsonElement GoodArgs = Json("""{"price_vnd":600000000,"horizon_months":60}""");
    private static readonly JsonElement BadArgs = Json("""{"price_vnd":"sáu trăm triệu","horizon_months":60}""");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class RecordingSource(IFinancialDataSource inner) : IFinancialDataSource
    {
        public List<Guid> UsersSeen { get; } = [];

        public Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default)
        {
            UsersSeen.Add(userId);
            return inner.GetMonthlySeriesAsync(userId, months, now, ct);
        }

        public Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            UsersSeen.Add(userId);
            return inner.GetCategorySpendAsync(userId, from, to, ct);
        }

        public Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default)
        {
            UsersSeen.Add(userId);
            return inner.GetTotalBalanceAsync(userId, ct);
        }

        public Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default)
        {
            UsersSeen.Add(userId);
            return inner.GetIncomeSegmentOverrideAsync(userId, ct);
        }
    }

    private sealed class FakeAi : IAgentAiClient
    {
        public AgentGateResult Gate { get; set; } = new(true, "finance_cue", null);
        public Func<AgentPlanRequest, AgentPlanResult> Plan { get; set; } = _ => new AgentPlanResult("forecast_goal", [new AgentPlanStep("forecast_goal", GoodArgs)], null, null);
        public Func<IReadOnlyList<Evidence>, AgentComposeResult> Compose { get; set; } =
            evidence => new AgentComposeResult([new AgentClaim("Bạn có số dư và mục tiêu.", ["balance", "price"])], false, false, null);
        public Exception? GateError { get; set; }
        public Exception? PlanError { get; set; }
        public Exception? ComposeError { get; set; }
        public List<AgentPlanRequest> PlanCalls { get; } = [];
        public int ComposeCalls { get; private set; }
        public List<AgentMemoryEntry> Memory { get; } = [];
        public Exception? MemoryError { get; set; }

        public Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default)
            => MemoryError is null ? Task.FromResult<IReadOnlyList<AgentMemoryEntry>>(Memory.ToList()) : Task.FromException<IReadOnlyList<AgentMemoryEntry>>(MemoryError);

        public Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default)
        {
            Memory.RemoveAll(m => m.Key == key);
            Memory.Add(new AgentMemoryEntry(key, value, Now));
            return Task.CompletedTask;
        }

        public Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default)
            => Task.FromResult(Memory.RemoveAll(m => key is null || m.Key == key));

        public Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default)
            => GateError is null ? Task.FromResult(Gate) : Task.FromException<AgentGateResult>(GateError);

        public Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default)
        {
            PlanCalls.Add(request);
            return PlanError is null ? Task.FromResult(Plan(request)) : Task.FromException<AgentPlanResult>(PlanError);
        }

        public Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default)
        {
            ComposeCalls++;
            return ComposeError is null ? Task.FromResult(Compose(evidence)) : Task.FromException<AgentComposeResult>(ComposeError);
        }
    }

    /// <summary>Giả ICacheService: lưu JSON như Redis (nên bản ghi không giữ tham chiếu tới đối tượng gốc) và nhớ TTL.</summary>
    private sealed class FakeCache : ICacheService
    {
        public Dictionary<string, (string Json, TimeSpan Ttl)> Items { get; } = [];
        public bool Broken { get; set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (Broken)
            {
                throw new InvalidOperationException("Redis sập");
            }

            return Task.FromResult(Items.TryGetValue(key, out var item) ? JsonSerializer.Deserialize<T>(item.Json) : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            if (Broken)
            {
                throw new InvalidOperationException("Redis sập");
            }

            Items[key] = (JsonSerializer.Serialize(value), ttl);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Items.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static (AgentOrchestrator Orchestrator, FakeAi Ai, RecordingSource Source) Build(
        long[]? income = null, IncomeSegment? segmentOverride = null, FakeCache? cache = null, AgentTraceContext? trace = null)
    {
        var source = new RecordingSource(new InMemoryFinancialDataSource(income ?? Income, Spend.Take((income ?? Income).Length).ToArray(), 100_000_000, segmentOverride));
        var assumptions = new GetAssumptionsTool(Mock.Of<IEconomicAssumptionRepository>());
        var registry = new ToolRegistry([new ForecastGoalTool(source, assumptions), new GetAssumptionsAgentTool(assumptions)]);
        var ai = new FakeAi();
        return (new AgentOrchestrator(ai, new ToolExecutor(registry), new IncomeHistory(source), new ConversationStore(cache ?? new FakeCache()), trace ?? new AgentTraceContext(), () => Now), ai, source);
    }

    [Fact]
    public async Task the_trace_context_carries_the_answers_trace_id_and_the_asking_user()
    {
        var trace = new AgentTraceContext();
        var (orchestrator, _, _) = Build(trace: trace);
        var me = Guid.NewGuid();

        var answer = await orchestrator.AskAsync(me, "mua xe 600 triệu");

        trace.IsActive.Should().BeTrue();
        trace.TraceId.Should().Be(answer.TraceId);
        trace.UserId.Should().Be(me);
    }

    [Fact]
    public async Task the_trace_starts_before_the_first_call_so_even_a_refusal_is_traceable()
    {
        var trace = new AgentTraceContext();
        var (orchestrator, ai, _) = Build(trace: trace);
        ai.Gate = new AgentGateResult(false, "out_of_scope", "Ngoài phạm vi.");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "thời tiết");

        trace.TraceId.Should().Be(answer.TraceId);
    }

    [Fact]
    public async Task a_follow_up_reuses_the_price_and_horizon_of_the_previous_turn()
    {
        var (orchestrator, ai, _) = Build();
        var me = Guid.NewGuid();

        await orchestrator.AskAsync(me, "Mua xe 600 triệu sau 5 năm được không?");
        await orchestrator.AskAsync(me, "còn 3 năm thì sao?");

        ai.PlanCalls[0].State.Should().BeNull();
        ai.PlanCalls[1].State!.Slots.Should().Contain(new KeyValuePair<string, string>("price_vnd", "600000000"));
        ai.PlanCalls[1].State!.Slots.Should().Contain(new KeyValuePair<string, string>("horizon_months", "60"));
        ai.PlanCalls[1].State!.LastIntent.Should().Be("forecast_goal");
    }

    [Fact]
    public async Task conversation_state_is_per_user_has_a_ttl_and_stores_no_raw_text()
    {
        var cache = new FakeCache();
        var (orchestrator, ai, _) = Build(cache: cache);
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        await orchestrator.AskAsync(a, "Mua xe 600 triệu sau 5 năm được không?");
        ai.Plan = _ => new AgentPlanResult("other", [], "Bạn muốn hỏi gì về tài chính?", null); // B hỏi lại → không có lượt nào để nhớ
        await orchestrator.AskAsync(b, "xin chào, cho mình hỏi một câu");

        var stored = cache.Items.Single();
        stored.Key.Should().Contain(a.ToString());
        stored.Value.Ttl.Should().Be(AgentLimits.ConversationTtl);
        stored.Value.Json.Should().NotContain("Mua xe").And.NotContain("chào");
        ai.PlanCalls.Last().State.Should().BeNull(); // người dùng B không thấy ngữ cảnh của A
    }

    [Fact]
    public async Task the_conversation_starts_over_after_max_turns()
    {
        var (orchestrator, ai, _) = Build();
        var me = Guid.NewGuid();

        for (var i = 0; i < AgentLimits.MaxTurns; i++)
        {
            await orchestrator.AskAsync(me, "Mua xe 600 triệu được không?");
        }

        await orchestrator.AskAsync(me, "còn 3 năm thì sao?");

        ai.PlanCalls[AgentLimits.MaxTurns - 1].State.Should().NotBeNull();
        ai.PlanCalls[AgentLimits.MaxTurns].State.Should().BeNull();
    }

    [Fact]
    public async Task the_users_memory_is_passed_to_the_planner_as_data()
    {
        var (orchestrator, ai, _) = Build();
        ai.Memory.Add(new AgentMemoryEntry("preferred_method", "phong bì", Now));

        await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        ai.PlanCalls[0].Memory.Should().ContainSingle().Which.Should().Be(new AgentMemoryItem("preferred_method", "phong bì"));
    }

    [Fact]
    public async Task an_unreachable_memory_store_or_a_broken_cache_never_breaks_the_turn()
    {
        var cache = new FakeCache { Broken = true };
        var (orchestrator, ai, _) = Build(cache: cache);
        ai.MemoryError = new AIServiceUnavailableException("sập");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Status.Should().Be(AgentStatus.Answered);
        ai.PlanCalls[0].Memory.Should().BeNull();
        ai.PlanCalls[0].State.Should().BeNull();
    }

    [Fact]
    public async Task out_of_scope_stops_before_planning()
    {
        var (orchestrator, ai, source) = Build();
        ai.Gate = new AgentGateResult(false, "investment_advice", "Mình không tư vấn đầu tư.");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "nên mua coin nào?");

        answer.Status.Should().Be(AgentStatus.Refused);
        answer.Message.Should().Be("Mình không tư vấn đầu tư.");
        answer.Claims.Should().BeEmpty();
        ai.PlanCalls.Should().BeEmpty();
        ai.ComposeCalls.Should().Be(0);
        source.UsersSeen.Should().BeEmpty();
    }

    [Fact]
    public async Task clarify_returns_question_without_running_tools()
    {
        var (orchestrator, ai, source) = Build();
        ai.Plan = _ => new AgentPlanResult("forecast_goal", [], "Bạn định mua món giá bao nhiêu?", null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe được không");

        answer.Status.Should().Be(AgentStatus.Clarify);
        answer.Message.Should().Be("Bạn định mua món giá bao nhiêu?");
        ai.ComposeCalls.Should().Be(0);
        source.UsersSeen.Should().Contain(_ => true); // chỉ đọc để xác định nhóm thu nhập cho bộ lập kế hoạch
        answer.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task runs_only_allowlisted_tools()
    {
        var (orchestrator, ai, source) = Build();
        ai.Plan = _ => new AgentPlanResult("x", [new AgentPlanStep("drop_database", Json("{}"))], null, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Status.Should().Be(AgentStatus.Clarify);
        answer.Evidence.Should().BeEmpty();
        ai.ComposeCalls.Should().Be(0);
        source.UsersSeen.Should().HaveCount(2); // chỉ lần đọc nhóm thu nhập (chuỗi tháng + nhóm tự chọn); công cụ lạ không đọc thêm gì
    }

    [Fact]
    public async Task replans_at_most_twice_when_the_tool_arguments_are_rejected()
    {
        var (orchestrator, ai, _) = Build();
        ai.Plan = _ => new AgentPlanResult("forecast_goal", [new AgentPlanStep("forecast_goal", BadArgs)], null, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        ai.PlanCalls.Should().HaveCount(1 + AgentLimits.MaxReplans);
        ai.PlanCalls[1].Feedback.Should().ContainEquivalentOf(new AgentPlanFeedback("forecast_goal", "invalid_argument:price_vnd"));
        answer.Status.Should().Be(AgentStatus.Clarify);
    }

    [Fact]
    public async Task a_second_plan_that_fixes_the_arguments_is_used()
    {
        var (orchestrator, ai, _) = Build();
        ai.Plan = request => new AgentPlanResult("forecast_goal", [new AgentPlanStep("forecast_goal", request.Feedback is null ? BadArgs : GoodArgs)], null, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        ai.PlanCalls.Should().HaveCount(2);
        answer.Status.Should().Be(AgentStatus.Answered);
    }

    [Fact]
    public async Task the_planner_is_told_the_allowed_tools_the_step_limit_and_the_income_segment()
    {
        var (orchestrator, ai, _) = Build(segmentOverride: IncomeSegment.Variable);

        await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        var request = ai.PlanCalls[0];
        request.Segment.Should().Be("variable");
        request.MaxSteps.Should().Be(AgentLimits.MaxPlanSteps);
        request.Tools.Select(t => t.Name).Should().BeEquivalentTo("forecast_goal", "get_assumptions");
        request.Text.Should().Be("mua xe 600 triệu");
    }

    [Fact]
    public async Task more_steps_than_the_limit_are_ignored()
    {
        var (orchestrator, ai, _) = Build();
        ai.Plan = _ => new AgentPlanResult("x", Enumerable.Repeat(new AgentPlanStep("forecast_goal", GoodArgs), AgentLimits.MaxPlanSteps + 3).ToArray(), null, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Evidence.Count(e => e.Id == "price").Should().Be(1); // cùng một bằng chứng không bị nhân bản
        answer.Status.Should().Be(AgentStatus.Answered);
    }

    [Fact]
    public async Task a_short_history_is_insufficient_data_without_calling_compose()
    {
        var (orchestrator, ai, _) = Build(income: [25_000_000, 25_000_000]);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Status.Should().Be(AgentStatus.InsufficientData);
        answer.Message.Should().Contain(AgentLimits.MinHistoryMonths.ToString());
        ai.ComposeCalls.Should().Be(0);
    }

    [Fact]
    public async Task a_grounded_answer_carries_evidence_with_sources_and_the_verdict()
    {
        var (orchestrator, _, _) = Build();

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Status.Should().Be(AgentStatus.Answered);
        answer.TraceId.Should().NotBeEmpty();
        answer.UsedTemplate.Should().BeFalse();
        answer.Verdict.Should().Be("feasible");
        answer.Evidence.Single(e => e.Id == "price").Should().Match<AgentEvidenceDto>(e => e.ValueVnd == 600_000_000 && e.Source == "user_entered");
        answer.Evidence.Single(e => e.Id == "balance").Source.Should().Be("confirmed");
        answer.Claims.Single().EvidenceIds.Should().Equal("balance", "price");
    }

    [Fact]
    public async Task evidence_ids_not_from_this_run_are_dropped()
    {
        var (orchestrator, ai, _) = Build();
        ai.Compose = _ => new AgentComposeResult(
            [new AgentClaim("Số dư của bạn.", ["balance", "ghost-from-another-user"]), new AgentClaim("Chỉ có mã lạ.", ["ghost"])], false, false, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Claims.Should().ContainSingle().Which.EvidenceIds.Should().Equal("balance");
    }

    [Fact]
    public async Task compose_unavailable_falls_back_to_template()
    {
        var (orchestrator, ai, _) = Build();
        ai.ComposeError = new AIServiceUnavailableException("ai-service đã sập giữa vòng");

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.Status.Should().Be(AgentStatus.Answered);
        answer.UsedTemplate.Should().BeTrue();
        answer.Claims.Should().NotBeEmpty();
        answer.Claims.Should().OnlyContain(c => c.EvidenceIds.Count > 0);
        answer.Claims.Select(c => c.Text).Should().Contain(t => t.Contains("600.000.000 đồng"));
    }

    [Fact]
    public async Task when_every_claim_loses_its_evidence_the_template_takes_over()
    {
        var (orchestrator, ai, _) = Build();
        ai.Compose = _ => new AgentComposeResult([new AgentClaim("Toàn mã lạ.", ["ghost"])], false, false, null);

        var answer = await orchestrator.AskAsync(Guid.NewGuid(), "mua xe 600 triệu");

        answer.UsedTemplate.Should().BeTrue();
        answer.Claims.Should().OnlyContain(c => c.EvidenceIds.All(id => answer.Evidence.Any(e => e.Id == id)));
    }

    [Fact]
    public async Task cross_user_tool_call_is_isolated()
    {
        var (orchestrator, _, source) = Build();
        var me = Guid.NewGuid();

        await orchestrator.AskAsync(me, "mua xe 600 triệu");

        source.UsersSeen.Should().NotBeEmpty();
        source.UsersSeen.Distinct().Should().Equal(me);
    }

    [Fact]
    public async Task an_unreachable_ai_service_before_any_evidence_is_a_503_not_a_made_up_answer()
    {
        var (orchestrator, ai, _) = Build();
        ai.GateError = new AIServiceUnavailableException("sập");
        (await ((Func<Task>)(async () => await orchestrator.AskAsync(Guid.NewGuid(), "x"))).Should().ThrowAsync<AIServiceUnavailableException>()).Which.Should().NotBeNull();

        var (second, ai2, _) = Build();
        ai2.PlanError = new AIServiceUnavailableException("sập");
        await ((Func<Task>)(async () => await second.AskAsync(Guid.NewGuid(), "mua xe 600 triệu"))).Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public void the_local_template_is_independent_of_the_ai_service_wording()
    {
        var evidence = new List<Evidence>
        {
            new("price", "goal_price", 600_000_000, "Giá mục tiêu", "VND", EvidenceSource.UserEntered, Now, 0.9),
            new("verdict", "goal_verdict", null, "verdict: tight", "label", EvidenceSource.Estimated, Now, 0.5),
        };

        var claims = LocalTemplateComposer.Compose(evidence);

        claims.Select(c => c.Text).Should().Contain(t => t.Contains("600.000.000 đồng") && t.Contains("bạn nhập") && t.Contains("07/11/2026"));
        claims.Should().OnlyContain(c => c.EvidenceIds.Count == 1);
    }
}
