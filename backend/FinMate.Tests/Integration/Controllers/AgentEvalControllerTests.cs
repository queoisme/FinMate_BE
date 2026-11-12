using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Endpoint đánh giá: tắt thì 404, cần khóa nội bộ, và khi chạy thì công cụ đọc ĐÚNG dữ liệu giả lập gửi kèm, kèm vết công cụ và số token.</summary>
[Collection("Integration")]
public class AgentEvalControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Run = "/api/v1/internal/agent/run";
    private const string Key = "dummy-ai-service-key-for-tests-min-32-chars";
    private static readonly JsonElement Args = JsonDocument.Parse("""{"price_vnd":600000000,"horizon_months":24}""").RootElement.Clone();

    private sealed class FakeAi : IAgentAiClient
    {
        public bool? SkipVerifier { get; private set; }

        public string Tool { get; set; } = "forecast_goal";

        public JsonElement ToolArgs { get; set; } = Args;

        public Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default) => Task.FromResult(new AgentGateResult(true, "finance_cue", null));

        public Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default)
            => Task.FromResult(new AgentPlanResult(Tool, [new AgentPlanStep(Tool, ToolArgs)], null, new AgentUsage(100, 20, "fake", "fake")));

        public Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default)
        {
            SkipVerifier = skipVerifier;
            return Task.FromResult(new AgentComposeResult([], false, false, new AgentUsage(50, 10, "fake", "fake")));
        }

        public Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AgentMemoryEntry>>([]);

        public Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default) => Task.FromResult(0);
    }

    private record Step(string Tool, string Status);
    private record Usage(int InputTokens, int OutputTokens);
    private record Answer(string Status, List<JsonElement> Evidence, string? Verdict);
    private record Data(Answer Answer, List<Step> Trace, Usage Usage);
    private record Body(bool Success, Data Data);

    private readonly AuthApiFactory _factory;
    private readonly FakeAi _ai = new();

    public AgentEvalControllerTests(AuthApiFactory factory) => _factory = factory;

    private HttpClient Client(bool enabled) => _factory.WithWebHostBuilder(builder =>
    {
        if (enabled)
        {
            builder.UseSetting("AGENT_EVAL_ENABLED", "true");
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentAiClient>();
            services.AddSingleton<IAgentAiClient>(_ai);
        });
    }).CreateClient();

    private static HttpRequestMessage Post(string? key, bool verifier = true)
    {
        var income = Enumerable.Repeat(20_000_000L, 12).ToArray();
        var spend = Enumerable.Repeat(12_000_000L, 12).ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new
            {
                snapshot = new { monthlyIncome = income, monthlySpend = spend, totalBalance = 100_000_000L },
                question = "Mua xe 600 triệu sau 2 năm được không?",
                flags = new { verifier, rag = true },
            }),
        };
        if (key is not null)
        {
            request.Headers.Add("X-Eval-Key", key);
        }

        return request;
    }

    [Fact]
    public async Task WhenTheFlagIsOff_TheEndpointDoesNotExist()
        => (await Client(false).SendAsync(Post(Key))).StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task WithoutTheInternalKey_Is401(string? key)
        => (await Client(true).SendAsync(Post(key))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task TheToolsReadTheSnapshot_AndTheResponseCarriesTraceAndUsage()
    {
        var response = await Client(true).SendAsync(Post(Key));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<Body>(JsonOptions))!.Data;
        data.Answer.Status.Should().Be("answered");
        data.Answer.Evidence.Should().NotBeEmpty();
        data.Trace.Should().Contain(new Step("forecast_goal", "Ok"));
        data.Usage.Should().Be(new Usage(150, 30));
        _ai.SkipVerifier.Should().BeFalse();
    }

    [Fact]
    public async Task TheVerifierFlagOff_IsPassedToCompose()
    {
        await Client(true).SendAsync(Post(Key, verifier: false));

        _ai.SkipVerifier.Should().BeTrue();
    }

    [Fact]
    public async Task SnapshotWithCategorySpend_ReachesTheTool()
    {
        _ai.Tool = "spending_summary";
        _ai.ToolArgs = JsonDocument.Parse("""{"months":3}""").RootElement.Clone();
        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new
            {
                snapshot = new
                {
                    monthlyIncome = Enumerable.Repeat(20_000_000L, 12).ToArray(),
                    monthlySpend = Enumerable.Repeat(12_000_000L, 12).ToArray(),
                    totalBalance = 100_000_000L,
                    categorySpend = new[]
                    {
                        new { slug = "transport", name = "Di chuyển", spentVnd = 4_000_000L },
                        new { slug = "food", name = "Ăn uống", spentVnd = 9_000_000L },
                    },
                },
                question = "3 tháng qua tôi tiêu vào đâu nhiều nhất?",
                flags = new { verifier = true, rag = true },
            }),
        };
        request.Headers.Add("X-Eval-Key", Key);

        var response = await Client(true).SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<Body>(JsonOptions))!.Data;
        data.Trace.Should().Contain(new Step("spending_summary", "Ok"));
        var values = data.Answer.Evidence.ToDictionary(e => e.GetProperty("id").GetString()!, e => e.GetProperty("valueVnd").GetInt64());
        values.Should().Contain("spend_total", 13_000_000L).And.Contain("cat_1", 9_000_000L).And.Contain("cat_2", 4_000_000L);
    }
}
