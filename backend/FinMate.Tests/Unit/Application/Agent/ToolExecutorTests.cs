using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

public class ToolExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly long[] Income = [25_000_000, 25_000_000, 25_000_000, 25_000_000, 25_000_000, 25_000_000];
    private static readonly long[] Spend = [15_000_000, 15_000_000, 15_000_000, 15_000_000, 15_000_000, 15_000_000];

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

    private static (ToolExecutor Executor, RecordingSource Source) Build(long balance = 100_000_000, bool withAssumption = true)
    {
        var source = new RecordingSource(new InMemoryFinancialDataSource(Income, Spend, balance));
        var repo = new Mock<IEconomicAssumptionRepository>();
        if (withAssumption)
        {
            repo.Setup(r => r.GetApprovedAsync("general_price_growth", It.IsAny<CancellationToken>())).ReturnsAsync(new EconomicAssumption
            {
                Id = Guid.NewGuid(), Key = "general_price_growth", RateBps = 0, Unit = "pct_per_year", SourceName = "GSO", SourceUrl = "https://www.gso.gov.vn/",
                AsOf = Now.AddDays(-5), Version = 1, Status = AssumptionStatus.Approved,
            });
        }

        var assumptions = new GetAssumptionsTool(repo.Object);
        var registry = new ToolRegistry([new ForecastGoalTool(source, assumptions), new GetAssumptionsAgentTool(assumptions)]);
        return (new ToolExecutor(registry), source);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void allowlist_is_exactly_the_registered_tools()
        => Build().Executor.Registry.Allowlist.Should().BeEquivalentTo("forecast_goal", "get_assumptions");

    [Fact]
    public async Task unknown_tool_is_rejected()
    {
        var result = await Build().Executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "drop_database", Args("{}"));

        result.Status.Should().Be(ToolStatus.Rejected);
        result.Code.Should().Be("unknown_tool");
    }

    [Theory]
    [InlineData("""{"horizon_months":24}""", "missing_argument:price_vnd")]
    [InlineData("""{"price_vnd":600000000}""", "missing_argument:horizon_months")]
    [InlineData("""{"price_vnd":"600 triệu","horizon_months":24}""", "invalid_argument:price_vnd")]
    [InlineData("""{"price_vnd":600000000.5,"horizon_months":24}""", "invalid_argument:price_vnd")]
    [InlineData("""{"price_vnd":0,"horizon_months":24}""", "invalid_argument:price_vnd")]
    [InlineData("""{"price_vnd":600000000,"horizon_months":0}""", "invalid_argument:horizon_months")]
    [InlineData("""{"price_vnd":600000000,"horizon_months":24,"user_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", "unknown_argument:user_id")]
    public async Task bad_arguments_are_rejected_before_any_data_is_read(string args, string code)
    {
        var (executor, source) = Build();

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "forecast_goal", Args(args));

        result.Status.Should().Be(ToolStatus.Rejected);
        result.Code.Should().Be(code);
        source.UsersSeen.Should().BeEmpty();
    }

    [Fact]
    public async Task cross_user_tool_call_is_isolated()
    {
        var (executor, source) = Build();
        var me = Guid.NewGuid();

        await executor.ExecuteAsync(new ToolContext(me, Now), "forecast_goal", Args("""{"price_vnd":600000000,"horizon_months":24}"""));

        source.UsersSeen.Should().NotBeEmpty();
        source.UsersSeen.Distinct().Should().Equal(me);
    }

    [Fact]
    public async Task forecast_goal_returns_cited_evidence_with_sources_and_confidence()
    {
        var (executor, _) = Build();

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "forecast_goal", Args("""{"price_vnd":600000000,"horizon_months":60}"""));

        result.Status.Should().Be(ToolStatus.Ok);
        var byId = result.Evidence.ToDictionary(e => e.Id);
        byId["price"].ValueVnd.Should().Be(600_000_000);
        byId["price"].Source.Should().Be(EvidenceSource.UserEntered);
        byId["balance"].ValueVnd.Should().Be(100_000_000);
        byId["balance"].Source.Should().Be(EvidenceSource.Confirmed);
        byId["scenario_base"].ValueVnd.Should().Be(10_000_000); // dư mỗi tháng của kịch bản cơ sở
        byId["scenario_base"].Source.Should().Be(EvidenceSource.Estimated);
        byId["scenario_base"].Confidence.Should().BeLessThanOrEqualTo(0.7);
        byId["verdict"].Text.Should().Contain("feasible");
        result.Evidence.Should().OnlyContain(e => e.Confidence >= 0 && e.Confidence <= 1);
    }

    [Fact]
    public async Task short_history_is_insufficient_data_not_a_guess()
    {
        var source = new InMemoryFinancialDataSource([25_000_000, 25_000_000], [15_000_000, 15_000_000], 100_000_000);
        var tool = new ForecastGoalTool(source, new GetAssumptionsTool(Mock.Of<IEconomicAssumptionRepository>()));

        var result = await new ToolExecutor(new ToolRegistry([tool])).ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "forecast_goal", Args("""{"price_vnd":600000000,"horizon_months":24}"""));

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("history_too_short");
        result.Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task missing_assumption_is_disclosed_as_flat_price_not_invented()
    {
        var (executor, _) = Build(withAssumption: false);

        var result = await executor.ExecuteAsync(new ToolContext(Guid.NewGuid(), Now), "forecast_goal", Args("""{"price_vnd":600000000,"horizon_months":60}"""));

        result.Status.Should().Be(ToolStatus.Ok);
        var growth = result.Evidence.Single(e => e.Id == "price_growth");
        growth.Source.Should().Be(EvidenceSource.Estimated);
        growth.Text.Should().ContainEquivalentOf("chưa có giả định");
    }

    [Fact]
    public async Task get_assumptions_returns_the_approved_one_or_insufficient_data()
    {
        var (executor, _) = Build();
        var ctx = new ToolContext(Guid.NewGuid(), Now);

        var found = await executor.ExecuteAsync(ctx, "get_assumptions", Args("""{"key":"general_price_growth"}"""));
        var missing = await executor.ExecuteAsync(ctx, "get_assumptions", Args("""{"key":"nonexistent_key"}"""));

        found.Status.Should().Be(ToolStatus.Ok);
        found.Evidence.Single().Source.Should().Be(EvidenceSource.Document);
        missing.Status.Should().Be(ToolStatus.InsufficientData);
        missing.Code.Should().Be("assumption_missing");
    }
}
