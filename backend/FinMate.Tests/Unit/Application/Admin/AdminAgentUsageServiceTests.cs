using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>"Token và chi phí AI": giá lấy từ cấu hình hệ thống theo tên mô hình, nhân với token; thiếu giá thì chỉ có token, không đoán tiền.</summary>
public class AdminAgentUsageServiceTests
{
    private const string Body = """
        {"days":30,"model":"gemini-3.1-flash-lite","provider":"gemini",
         "total":{"runs":2,"input_tokens":2000000,"output_tokens":1000000},
         "by_step":[{"step":"plan","rows":2,"input_tokens":1000000,"output_tokens":0},{"step":"compose","rows":2,"input_tokens":1000000,"output_tokens":1000000}],
         "by_day":[{"day":"2026-11-26","runs":2,"input_tokens":2000000,"output_tokens":1000000}]}
        """;

    private sealed class FakeAi : IAiRulesAdminClient
    {
        public AiRulesRequest? Call { get; private set; }

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            Call = request;
            using var doc = JsonDocument.Parse(Body);
            return Task.FromResult<JsonElement?>(doc.RootElement.Clone());
        }
    }

    private sealed class FakeConfig(AgentSettings agent) : ISystemConfigProvider
    {
        public Task<SystemConfigSnapshot> GetAsync(CancellationToken ct = default)
            => Task.FromResult(new SystemConfigSnapshot(1, new SystemConfigDocument { Agent = agent }, null));

        public void Invalidate() { }
    }

    private static async Task<JsonElement> RunAsync(AgentSettings agent, FakeAi? ai = null)
        => (await new AdminAgentUsageService(ai ?? new FakeAi(), new FakeConfig(agent)).SummaryAsync(new Dictionary<string, string?> { ["days"] = "30" }))!.Value;

    [Fact]
    public async Task WithPricesForTheModel_AddsCostToTotalStepsAndDays_AndAsksTheUsageArea()
    {
        var ai = new FakeAi();
        var agent = new AgentSettings
        {
            LlmInputUsdPerMillion = new Dictionary<string, double> { ["Gemini-3.1-Flash-Lite "] = 0.25, ["other"] = 99 },  // khớp không phân biệt hoa thường và khoảng trắng
            LlmOutputUsdPerMillion = new Dictionary<string, double> { ["gemini-3.1-flash-lite"] = 1.5 },
        };

        var result = await RunAsync(agent, ai);

        ai.Call!.Area.Should().Be(AiRulesRequest.AgentUsageArea);
        ai.Call.Path.Should().Be("summary");
        ai.Call.Query!["days"].Should().Be("30");
        result.GetProperty("cost").GetProperty("priced").GetBoolean().Should().BeTrue();
        result.GetProperty("total").GetProperty("cost_usd").GetDouble().Should().Be(2.0);  // 2 triệu vào × 0,25 + 1 triệu ra × 1,5 = 0,5 + 1,5
        result.GetProperty("by_step")[0].GetProperty("cost_usd").GetDouble().Should().Be(0.25);
        result.GetProperty("by_day")[0].GetProperty("cost_usd").GetDouble().Should().Be(2.0);
        result.GetProperty("provider").GetString().Should().Be("gemini");  // phần còn lại của phản hồi đi nguyên văn
    }

    [Fact]
    public async Task WithoutAPriceForTheModel_KeepsTokensAndLeavesEveryCostNull()
    {
        var onlyInput = new AgentSettings { LlmInputUsdPerMillion = new Dictionary<string, double> { ["gemini-3.1-flash-lite"] = 0.25 } };

        foreach (var agent in new[] { new AgentSettings(), onlyInput })
        {
            var result = await RunAsync(agent);

            result.GetProperty("cost").GetProperty("priced").GetBoolean().Should().BeFalse();
            result.GetProperty("total").GetProperty("cost_usd").ValueKind.Should().Be(JsonValueKind.Null);
            result.GetProperty("by_day")[0].GetProperty("cost_usd").ValueKind.Should().Be(JsonValueKind.Null);
            result.GetProperty("total").GetProperty("input_tokens").GetInt64().Should().Be(2_000_000);
        }
    }

    [Fact]
    public void TheConfigValidator_AcceptsPricesAndRejectsNegativeOnesOrEmptyModelNames()
    {
        var validator = new SystemConfigDocumentValidator();
        SystemConfigDocument With(AgentSettings agent) => new() { Agent = agent };

        validator.Validate(With(new AgentSettings { LlmInputUsdPerMillion = new Dictionary<string, double> { ["m"] = 0.25 } })).Errors
            .Should().NotContain(e => e.ErrorMessage.Contains("Giá token"));
        validator.Validate(With(new AgentSettings { LlmOutputUsdPerMillion = new Dictionary<string, double> { ["m"] = -1 } })).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("Giá token ra"));
        validator.Validate(With(new AgentSettings { LlmInputUsdPerMillion = new Dictionary<string, double> { [" "] = 1 } })).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("Giá token vào"));
    }
}
