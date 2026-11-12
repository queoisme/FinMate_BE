using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>POST /api/v1/agent/ask với ai-service giả: xác thực, kiểm câu hỏi, các nhánh trả lời, hạn mức theo người dùng. Người dùng mới chưa có lịch sử thu nhập.</summary>
[Collection("Integration")]
public class AgentControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Ask = "/api/v1/agent/ask";
    private static readonly JsonElement ForecastArgs = JsonDocument.Parse("""{"price_vnd":600000000,"horizon_months":24}""").RootElement.Clone();

    private sealed class FakeAi : IAgentAiClient
    {
        public AgentGateResult Gate { get; set; } = new(true, "finance_cue", null);
        public AgentPlanResult Plan { get; set; } = new("forecast_goal", [new AgentPlanStep("forecast_goal", ForecastArgs)], null, null);
        public Exception? GateError { get; set; }
        public List<string> Questions { get; } = [];
        public int ComposeCalls { get; private set; }
        public List<(string Key, string Value)> Puts { get; } = [];

        public Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentMemoryEntry>>([new AgentMemoryEntry("preferred_method", "phong bì", DateTimeOffset.UtcNow)]);

        public Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default)
        {
            Puts.Add((key, value));
            return Task.CompletedTask;
        }

        public Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default) => Task.FromResult(key is null ? 2 : 1);

        public Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default)
        {
            Questions.Add(text);
            return GateError is null ? Task.FromResult(Gate) : Task.FromException<AgentGateResult>(GateError);
        }

        public Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default) => Task.FromResult(Plan);

        public Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default)
        {
            ComposeCalls++;
            return Task.FromResult(new AgentComposeResult([], false, false, null));
        }
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ErrorData(string Code);
    private record ErrorBody(bool Success, ErrorData Error);
    private record AnswerData(string Status, string? Message, List<object> Claims, List<object> Evidence, string? Verdict, Guid TraceId, bool UsedTemplate);
    private record AnswerBody(bool Success, AnswerData Data);

    private readonly AuthApiFactory _factory;
    private readonly FakeAi _ai = new();
    private readonly HttpClient _client;

    public AgentControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = Client(_ai);
    }

    private HttpClient Client(FakeAi ai, string? agentPerMinute = null)
        => _factory.WithWebHostBuilder(builder =>
        {
            if (agentPerMinute is not null)
            {
                builder.UseSetting("RATE_LIMIT_AGENT_PER_MINUTE", agentPerMinute);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAgentAiClient>();
                services.AddSingleton<IAgentAiClient>(ai);
            });
        }).CreateClient();

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Agent Test User"));
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private static HttpRequestMessage Post(string question, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Ask) { Content = JsonContent.Create(new { question }) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task Anonymous_Is401()
        => (await _client.SendAsync(Post("mua xe 600 triệu", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task ABlankQuestion_Is422_AndNothingReachesTheAiService(string question)
    {
        var token = await TokenAsync(_client);

        var response = await _client.SendAsync(Post(question, token));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("invalid_question");
        _ai.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task ATooLongQuestion_Is422()
    {
        var token = await TokenAsync(_client);

        (await _client.SendAsync(Post(new string('a', 501), token))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AnOutOfScopeQuestion_IsRefused_WithTheMessageFromTheGate()
    {
        _ai.Gate = new AgentGateResult(false, "investment_advice", "Mình không tư vấn đầu tư.");
        var token = await TokenAsync(_client);

        var response = await _client.SendAsync(Post("nên mua coin nào?", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<AnswerBody>(JsonOptions))!.Data;
        body.Status.Should().Be("refused");
        body.Message.Should().Be("Mình không tư vấn đầu tư.");
        body.TraceId.Should().NotBeEmpty();
        _ai.ComposeCalls.Should().Be(0);
    }

    [Fact]
    public async Task ANewUserWithNoHistory_GetsInsufficientData_NotAGuess()
    {
        var token = await TokenAsync(_client);

        var response = await _client.SendAsync(Post("Mua xe 600 triệu sau 2 năm được không?", token));

        var body = (await response.Content.ReadFromJsonAsync<AnswerBody>(JsonOptions))!.Data;
        body.Status.Should().Be("insufficient_data");
        body.Message.Should().Contain(AgentLimits.MinHistoryMonths.ToString());
        body.Evidence.Should().BeEmpty();
        _ai.ComposeCalls.Should().Be(0);
    }

    [Fact]
    public async Task AClarifyingPlan_IsReturnedAsAQuestion()
    {
        _ai.Plan = new AgentPlanResult("forecast_goal", [], "Bạn định mua món giá bao nhiêu?", null);
        var token = await TokenAsync(_client);

        var body = (await (await _client.SendAsync(Post("mua xe được không", token))).Content.ReadFromJsonAsync<AnswerBody>(JsonOptions))!.Data;

        body.Status.Should().Be("clarify");
        body.Message.Should().Be("Bạn định mua món giá bao nhiêu?");
    }

    [Fact]
    public async Task WhenTheAiServiceIsDownBeforeAnyEvidence_TheAnswerIs503()
    {
        _ai.GateError = new AIServiceUnavailableException("sập");
        var token = await TokenAsync(_client);

        (await _client.SendAsync(Post("mua xe 600 triệu", token))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task TheQuestionIsTrimmed_AndIsTheOnlyThingSentToTheGate()
    {
        var token = await TokenAsync(_client);

        await _client.SendAsync(Post("   mua xe 600 triệu   ", token));

        _ai.Questions.Should().Equal("mua xe 600 triệu");
    }

    private record IntBody(bool Success, int Data);
    private record MemoryItemData(string Key, string Value);
    private record MemoryListBody(bool Success, List<MemoryItemData> Data);

    private static HttpRequestMessage Memory(HttpMethod method, string path, string? token, string? json = null)
    {
        var request = new HttpRequestMessage(method, $"/api/v1/agent/memory{path}");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return request;
    }

    [Fact]
    public async Task MemoryRoutes_RequireAuthentication()
    {
        foreach (var (method, path, json) in new[]
                 {
                     (HttpMethod.Get, "", null),
                     (HttpMethod.Put, "/preferred_method", """{"value":"phong bì"}"""),
                     (HttpMethod.Delete, "/preferred_method", null),
                     (HttpMethod.Delete, "", null),
                 })
        {
            (await _client.SendAsync(Memory(method, path, null, json))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task MemoryCanBeSetListedAndDeleted_ByItsOwner()
    {
        var token = await TokenAsync(_client);

        (await _client.SendAsync(Memory(HttpMethod.Put, "/preferred_method", token, """{"value":"  phong bì  "}"""))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var listed = await (await _client.SendAsync(Memory(HttpMethod.Get, "", token))).Content.ReadFromJsonAsync<MemoryListBody>(JsonOptions);
        var one = await (await _client.SendAsync(Memory(HttpMethod.Delete, "/preferred_method", token))).Content.ReadFromJsonAsync<IntBody>(JsonOptions);
        var all = await (await _client.SendAsync(Memory(HttpMethod.Delete, "", token))).Content.ReadFromJsonAsync<IntBody>(JsonOptions);

        _ai.Puts.Should().Equal(("preferred_method", "phong bì"));
        listed!.Data.Should().ContainSingle().Which.Key.Should().Be("preferred_method");
        one!.Data.Should().Be(1);
        all!.Data.Should().Be(2);
    }

    [Theory]
    [InlineData("/not_allowed", """{"value":"x"}""", "invalid_memory_key")]
    [InlineData("/preferred_method", """{"value":"   "}""", "invalid_memory_value")]
    public async Task MemoryWithAnInvalidKeyOrValue_Is422_AndNothingIsStored(string path, string json, string code)
    {
        var token = await TokenAsync(_client);

        var response = await _client.SendAsync(Memory(HttpMethod.Put, path, token, json));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
        _ai.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task MemoryValueOverTheLimit_Is422()
    {
        var token = await TokenAsync(_client);

        var response = await _client.SendAsync(Memory(HttpMethod.Put, "/preferred_method", token, $$"""{"value":"{{new string('x', 201)}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AskIsRateLimited_PerUser()
    {
        var ai = new FakeAi { Gate = new AgentGateResult(false, "out_of_scope", "Ngoài phạm vi.") };
        var limited = Client(ai, agentPerMinute: "2");
        var busy = await TokenAsync(limited);
        var other = await TokenAsync(limited);

        (await limited.SendAsync(Post("câu 1", busy))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await limited.SendAsync(Post("câu 2", busy))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await limited.SendAsync(Post("câu 3", busy))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.SendAsync(Post("câu 1", other))).StatusCode.Should().Be(HttpStatusCode.OK); // người khác không bị vạ lây
    }
}
