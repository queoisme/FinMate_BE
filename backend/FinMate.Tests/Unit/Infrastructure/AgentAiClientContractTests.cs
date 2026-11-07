using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Hợp đồng JSON backend ↔ ai-service cho vòng agent, kiểm bằng các tệp dùng chung ở <c>ai-service/tests/fixtures/agent_wire</c>.
/// Request: JSON mà <see cref="AgentAiClient"/> sinh ra PHẢI bằng tệp (bên Python kiểm tệp đó parse được bằng đúng các model <c>extra="forbid"</c>).
/// Response: tệp do bên Python xác nhận bằng model của nó; ở đây kiểm client đọc ra đúng. Tệp request chưa có thì được tạo từ kết quả thật rồi test báo lỗi một lần để người đọc duyệt.
/// </summary>
public class AgentAiClientContractTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));

    private sealed class StubHandler(string responseFixture) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Body { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method;
            Query = request.RequestUri!.Query;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReadFixture(responseFixture), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }

    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "ai-service", "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "ai-service", "tests", "fixtures", "agent_wire");
    }

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(FixtureDir(), name), Encoding.UTF8);

    private static AgentAiClient Client(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://ai-service/") }, NullLogger<AgentAiClient>.Instance);

    private static void AssertRequestMatchesFixture(string actualJson, string fixtureName)
    {
        var path = Path.Combine(FixtureDir(), fixtureName);
        var actual = JsonNode.Parse(actualJson)!;
        if (!File.Exists(path))
        {
            var pretty = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, actual.ToJsonString(pretty) + "\n", new UTF8Encoding(false));
            Assert.Fail($"Đã tạo {fixtureName} từ kết quả thật; hãy xem lại rồi chạy lại test.");
        }

        JsonNode.DeepEquals(actual, JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))).Should().BeTrue($"JSON gửi đi phải đúng {fixtureName}; thực tế: {actualJson}");
    }

    [Fact]
    public async Task gate_request_and_response_match_the_shared_fixtures()
    {
        var handler = new StubHandler("gate_response.json");

        var result = await Client(handler).GateAsync("nên mua coin nào?");

        handler.Path.Should().Be("/api/v1/agent/gate");
        AssertRequestMatchesFixture(handler.Body!, "gate_request.json");
        result.Should().Be(new AgentGateResult(false, "investment_advice", "Mình không tư vấn đầu tư."));
    }

    [Fact]
    public async Task plan_request_and_response_match_the_shared_fixtures()
    {
        var handler = new StubHandler("plan_response.json");
        var tools = new IAgentTool[] { new ForecastGoalTool(null!, null!), new GetAssumptionsAgentTool(null!) }.Select(t => t.Spec).ToList();
        var request = new AgentPlanRequest(
            "Mua xe 600 triệu sau 2 năm được không?", "variable", tools, AgentLimits.MaxPlanSteps,
            [new AgentPlanFeedback("forecast_goal", "invalid_argument:price_vnd")],
            new AgentPlanState("forecast_goal", new Dictionary<string, string> { ["price_vnd"] = "600000000" }),
            [new AgentMemoryItem("preferred_method", "phong bì")]);

        var result = await Client(handler).PlanAsync(request);

        handler.Path.Should().Be("/api/v1/agent/plan");
        AssertRequestMatchesFixture(handler.Body!, "plan_request.json");
        result.Intent.Should().Be("forecast_goal");
        result.Clarify.Should().BeNull();
        result.Steps.Should().ContainSingle().Which.Tool.Should().Be("forecast_goal");
        result.Steps[0].Args.GetProperty("price_vnd").GetInt64().Should().Be(600_000_000);
        result.Usage.Should().Be(new AgentUsage(500, 60, "gemini", "gemini-3.1-flash-lite"));
    }

    [Fact]
    public async Task compose_request_and_response_match_the_shared_fixtures()
    {
        var handler = new StubHandler("compose_response.json");
        var evidence = new List<Evidence>
        {
            new("balance", "total_balance", 100_000_000, "Tổng số dư các tài khoản", "VND", EvidenceSource.Confirmed, AsOf, 1.0),
            new("verdict", "goal_verdict", null, "verdict: tight", "label", EvidenceSource.Estimated, AsOf, 0.5, "Tổng cục Thống kê", "https://www.gso.gov.vn/"),
        };

        var result = await Client(handler).ComposeAsync("Mình còn bao nhiêu tiền?", evidence);

        handler.Path.Should().Be("/api/v1/agent/compose");
        AssertRequestMatchesFixture(handler.Body!, "compose_request.json");
        var claim = result.Claims.Should().ContainSingle().Subject;
        claim.Text.Should().Be("Bạn đang có 100 triệu.");
        claim.EvidenceIds.Should().Equal("balance");
        result.UsedTemplate.Should().BeFalse();
        result.NoData.Should().BeFalse();
        result.Usage.Should().Be(new AgentUsage(400, 80, "gemini", "gemini-3.1-flash-lite"));
    }

    [Fact]
    public async Task memory_requests_carry_a_sha256_hash_never_the_user_id_and_match_the_shared_fixtures()
    {
        var user = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        var put = new StubHandler("memory_put_response.json");
        await Client(put).PutMemoryAsync(user, "preferred_method", "phong bì");

        put.Path.Should().Be("/api/v1/agent/memory/preferred_method");
        AssertRequestMatchesFixture(put.Body!, "memory_put_request.json");
        put.Body.Should().NotContain(user.ToString());

        var list = new StubHandler("memory_list_response.json");
        var entries = await Client(list).GetMemoryAsync(user);

        list.Path.Should().Be("/api/v1/agent/memory");
        list.Query.Should().MatchRegex("^\\?user_id_hash=[0-9a-f]{64}$");
        list.Query.Should().NotContain(user.ToString());
        var entry = entries.Should().ContainSingle().Subject;
        (entry.Key, entry.Value).Should().Be(("preferred_method", "phong bì"));
        entry.UpdatedAt.Should().Be(new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task memory_delete_sends_the_hash_as_a_query_and_reads_the_count()
    {
        var handler = new StubHandler("memory_deleted_response.json");

        var deleted = await Client(handler).DeleteMemoryAsync(Guid.NewGuid(), null);

        handler.Method.Should().Be(HttpMethod.Delete);
        handler.Path.Should().Be("/api/v1/agent/memory");
        handler.Query.Should().MatchRegex("^\\?user_id_hash=[0-9a-f]{64}$");
        deleted.Should().Be(2);
    }

    [Fact]
    public async Task no_request_carries_a_user_identity()
    {
        var handler = new StubHandler("gate_response.json");

        await Client(handler).GateAsync("câu hỏi");

        handler.Body!.ToLowerInvariant().Should().NotContain("user");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task a_non_success_status_is_ai_service_unavailable(HttpStatusCode status)
    {
        var client = Client(new FuncHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"detail\":\"chứa nội dung bí mật\"}") }));

        var act = () => client.GateAsync("x");

        (await act.Should().ThrowAsync<AIServiceUnavailableException>()).Which.Message.Should().NotContain("bí mật");
    }

    [Fact]
    public async Task a_garbled_body_is_ai_service_unavailable()
    {
        var client = Client(new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("không phải JSON") }));

        await ((Func<Task>)(async () => await client.GateAsync("x"))).Should().ThrowAsync<AIServiceUnavailableException>();
    }
}
