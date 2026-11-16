using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Runway, bài kiểm tra giảm thu nhập, độ nhạy: phép tính thuần số nguyên đồng, rồi ba công cụ bọc quanh chúng.</summary>
public class ResilienceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));

    private static IncomeProfile Profile(IncomeSegment segment, long q10, long q50, long q90, int months = 6) => new(segment, q10, q50, q90, months, 0.1);

    // ---- runway ----

    [Theory]
    [InlineData(100_000_000L, 15_000_000L, 6)]
    [InlineData(89_999_999L, 15_000_000L, 5)]
    [InlineData(90_000_000L, 15_000_000L, 6)]
    [InlineData(14_999_999L, 15_000_000L, 0)]
    [InlineData(0L, 15_000_000L, 0)]
    [InlineData(-5_000_000L, 15_000_000L, 0)]
    public void runway_exact_integer_months(long balance, long spend, int expected)
        => Resilience.RunwayMonths(balance, spend).Should().Be(expected);

    [Fact]
    public void runway_needs_positive_spending_and_never_overflows()
    {
        var zero = () => Resilience.RunwayMonths(1, 0);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        Resilience.RunwayMonths(long.MaxValue, 1).Should().Be(int.MaxValue);
    }

    // ---- stress test ----

    [Fact]
    public void stress_zero_drop_equals_base()
    {
        var profile = Profile(IncomeSegment.Stable, 20_000_000, 20_000_000, 20_000_000);
        var simulation = GoalSimulator.Simulate(600_000_000, 100_000_000, profile, 10_000_000, 12_000_000, 14_000_000, 0, 24);

        var stress = Resilience.StressTest(profile, 12_000_000, 100_000_000, 24, 0);

        stress.MonthlyCapacityVnd.Should().Be(simulation.Scenarios.Single(s => s.Name == "base").MonthlyCapacityVnd).And.Be(8_000_000);
        stress.ProjectedBalanceVnd.Should().Be(100_000_000 + (8_000_000 * 24));
        stress.MonthsUntilEmpty.Should().BeNull();
    }

    [Fact]
    public void variable_segment_uses_q10_income_as_safe_income()
    {
        var variable = Profile(IncomeSegment.Variable, 5_000_000, 20_000_000, 40_000_000);
        var stable = variable with { Segment = IncomeSegment.Stable };

        Resilience.SafeIncome(variable).Should().Be(5_000_000);
        Resilience.SafeIncome(stable).Should().Be(20_000_000);
        Resilience.StressTest(variable, 12_000_000, 30_000_000, 6, 0).MonthlyCapacityVnd.Should().Be(-7_000_000);
    }

    [Fact]
    public void a_negative_month_reports_how_many_whole_months_the_balance_lasts()
    {
        var profile = Profile(IncomeSegment.Stable, 20_000_000, 20_000_000, 20_000_000);

        var stress = Resilience.StressTest(profile, 12_000_000, 30_000_000, 12, 50); // 10M − 12M = −2M/tháng

        stress.StressedIncomeVnd.Should().Be(10_000_000);
        stress.MonthlyCapacityVnd.Should().Be(-2_000_000);
        stress.MonthsUntilEmpty.Should().Be(15);
        stress.ProjectedBalanceVnd.Should().Be(30_000_000 - (2_000_000 * 12));
    }

    [Fact]
    public void an_empty_balance_with_a_negative_month_is_already_empty_and_a_total_drop_is_allowed()
    {
        var profile = Profile(IncomeSegment.Stable, 20_000_000, 20_000_000, 20_000_000);

        Resilience.StressTest(profile, 12_000_000, 0, 3, 100).MonthsUntilEmpty.Should().Be(0);
        var invalid = () => Resilience.StressTest(profile, 1, 1, 1, 101);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- sensitivity ----

    [Fact]
    public void sensitivity_ranks_largest_driver_first()
    {
        // dư cơ sở 5M/tháng, mục tiêu 27M, lệch 10%: thu nhập (18M/22M → dư 3M/7M → 9/4 tháng, chênh 5) > chi tiêu (16,5M/13,5M → dư 3,5M/6,5M → 8/5 tháng, chênh 3) > tăng giá (6 tháng cả hai phía, chênh 0)
        var profile = Profile(IncomeSegment.Stable, 20_000_000, 20_000_000, 20_000_000);

        var rows = Resilience.Sensitivity(27_000_000, 0, profile, 15_000_000, 15_000_000, 15_000_000, 0, 12, 1000);

        rows.Select(r => r.Driver).Should().Equal("income", "spend", "price_growth");
        rows.Select(r => r.Swing).Should().Equal(5, 3, 0);
        rows[0].Should().Be(new SensitivityRow("income", 9, 4, 5));
        rows[2].Should().Be(new SensitivityRow("price_growth", 6, 6, 0));
    }

    [Fact]
    public void a_driver_that_makes_the_goal_unreachable_counts_as_the_largest_swing()
    {
        var profile = Profile(IncomeSegment.Stable, 10_000_000, 10_000_000, 10_000_000);

        // dư đúng 0 → khi chi tiêu +1% thì dư âm, không đạt; khi −1% thì đạt: spend/income lệch cực lớn
        var rows = Resilience.Sensitivity(50_000_000, 0, profile, 10_000_000, 10_000_000, 10_000_000, 0, 24, 100);

        rows[0].Swing.Should().BeGreaterThan(100);
        rows.Single(r => r.Driver == "spend").MonthsIfUp.Should().BeNull();
    }

    [Fact]
    public void too_little_history_gives_no_sensitivity_rows()
    {
        var thin = Profile(IncomeSegment.Unknown, 1, 1, 1, months: 1);

        Resilience.Sensitivity(1_000_000, 0, thin, 1, 1, 1, 0, 12, 100).Should().OnlyContain(r => r.MonthsIfUp == null && r.MonthsIfDown == null && r.Swing == 0);
    }

    // ---- tools ----

    private static InMemoryFinancialDataSource Steady(long balance = 100_000_000)
        => new([20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000, 20_000_000], [12_000_000, 12_000_000, 12_000_000, 12_000_000, 12_000_000, 12_000_000], balance);

    private static JsonElement Args(object args) => JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();

    private static ToolContext Ctx() => new(Guid.NewGuid(), Now);

    [Fact]
    public async Task the_runway_tool_reports_whole_months_in_text_and_cites_balance_and_spend()
    {
        var result = await new RunwayTool(Steady(100_000_000)).RunAsync(Ctx(), Args(new { }));

        result.Status.Should().Be(ToolStatus.Ok);
        result.Evidence.Single(e => e.Id == "runway").Text.Should().Contain("8 tháng"); // 100M / 12M = 8 (làm tròn xuống)
        result.Evidence.Single(e => e.Id == "balance").Source.Should().Be(EvidenceSource.Confirmed);
        result.Evidence.Single(e => e.Id == "spend_q90").ValueVnd.Should().Be(12_000_000);
    }

    [Fact]
    public async Task all_three_tools_refuse_to_guess_with_too_little_history()
    {
        var thin = new InMemoryFinancialDataSource([20_000_000], [12_000_000], 1_000_000);
        var assumptions = new GetAssumptionsTool(Mock.Of<IEconomicAssumptionRepository>());

        (await new RunwayTool(thin).RunAsync(Ctx(), Args(new { }))).Code.Should().Be("history_too_short");
        (await new StressTestTool(thin).RunAsync(Ctx(), Args(new { months = 6, income_drop_percent = 30 }))).Code.Should().Be("history_too_short");
        (await new SensitivityTool(thin, assumptions).RunAsync(Ctx(), Args(new { price_vnd = 1_000_000, horizon_months = 12 }))).Code.Should().Be("history_too_short");
    }

    [Fact]
    public async Task the_stress_tool_gives_a_verdict_and_every_amount_is_an_integer_with_a_source()
    {
        var result = await new StressTestTool(Steady(30_000_000)).RunAsync(Ctx(), Args(new { months = 12, income_drop_percent = 50 }));

        result.Evidence.Single(e => e.Id == "stress_capacity").ValueVnd.Should().Be(-2_000_000);
        result.Evidence.Single(e => e.Id == "stress_balance").ValueVnd.Should().Be(6_000_000);
        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: tight").And.Contain("hết tiền sau khoảng 15 tháng");
        result.Evidence.Where(e => e.Unit == "VND").Should().OnlyContain(e => e.ValueVnd != null && e.Confidence > 0);
    }

    [Fact]
    public async Task the_stress_tool_says_infeasible_when_the_balance_runs_out_inside_the_horizon()
    {
        var result = await new StressTestTool(Steady(10_000_000)).RunAsync(Ctx(), Args(new { months = 12, income_drop_percent = 50 }));

        result.Evidence.Single(e => e.Id == "verdict").Text.Should().StartWith("verdict: infeasible");
    }

    [Fact]
    public async Task the_sensitivity_tool_lists_drivers_in_rank_order_and_uses_the_approved_growth_assumption()
    {
        var repository = new Mock<IEconomicAssumptionRepository>();
        repository.Setup(r => r.GetApprovedAsync("general_price_growth", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EconomicAssumption { Key = "general_price_growth", RateBps = 400, Unit = "pct_per_year", SourceName = "GSO", SourceUrl = "https://example.org", AsOf = Now, Version = 1, Status = AssumptionStatus.Approved });
        var tool = new SensitivityTool(Steady(0), new GetAssumptionsTool(repository.Object));

        var result = await tool.RunAsync(Ctx(), Args(new { price_vnd = 300_000_000, horizon_months = 120 }));

        result.Status.Should().Be(ToolStatus.Ok);
        var ranked = result.Evidence.Where(e => e.Id.StartsWith("sensitivity_")).ToList();
        ranked.Should().HaveCount(3);
        ranked.Select(e => e.Id).Should().Equal("sensitivity_1", "sensitivity_2", "sensitivity_3");
        ranked[0].Text.Should().Contain("#1");
        result.Evidence.Should().Contain(e => e.Id.StartsWith("assumption:general_price_growth"));
    }

    [Fact]
    public async Task the_executor_rejects_bad_arguments_for_the_new_tools()
    {
        var source = Steady();
        var executor = new ToolExecutor(new ToolRegistry([new RunwayTool(source), new StressTestTool(source), new SensitivityTool(source, new GetAssumptionsTool(Mock.Of<IEconomicAssumptionRepository>()))]));

        (await executor.ExecuteAsync(Ctx(), "stress_test", Args(new { months = 6, income_drop_percent = 101 }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(Ctx(), "stress_test", Args(new { months = 0, income_drop_percent = 10 }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(Ctx(), "runway", Args(new { user_id = Guid.NewGuid() }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(Ctx(), "sensitivity", Args(new { price_vnd = 1000, horizon_months = 12, delta_bps = 5000 }))).Status.Should().Be(ToolStatus.Rejected);
        (await executor.ExecuteAsync(Ctx(), "runway", Args(new { }))).Status.Should().Be(ToolStatus.Ok);
    }
}
