using FinMate.Application.Agent;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Mô phỏng "bao lâu nữa đủ tiền mua X": ba kịch bản thận trọng / cơ sở / lạc quan, toàn số nguyên đồng VND.</summary>
public class GoalSimulatorTests
{
    private static IncomeProfile Income(long q10, long q50, long q90, int months = 6)
        => new(IncomeSegment.Stable, q10, q50, q90, months, 0.1);

    /// <summary>Thu nhập và chi tiêu cố định: ba kịch bản trùng nhau, dư 10 triệu/tháng.</summary>
    private static GoalSimulation Flat(long price, long savings, int growthBps, int horizon)
        => GoalSimulator.Simulate(price, savings, Income(25_000_000, 25_000_000, 25_000_000), 15_000_000, 15_000_000, 15_000_000, growthBps, horizon);

    [Fact]
    public void zero_growth_exact_months()
    {
        var result = Flat(600_000_000, 100_000_000, 0, 60);

        result.Scenarios.Select(s => s.MonthsToReach).Should().Equal(50, 50, 50);
        result.Scenarios[1].MonthlyCapacityVnd.Should().Be(10_000_000);
        result.PriceAtHorizonVnd.Should().Be(600_000_000);
    }

    [Fact]
    public void savings_already_enough_means_zero_months_and_no_shortfall()
    {
        var result = Flat(600_000_000, 700_000_000, 0, 24);

        result.Scenarios.All(s => s.MonthsToReach == 0 && s.ShortfallVnd == 0).Should().BeTrue();
        result.Verdict.Should().Be(GoalVerdict.Feasible);
    }

    [Fact]
    public void price_growth_makes_it_take_longer()
    {
        Flat(600_000_000, 100_000_000, 500, 120).Scenarios[1].MonthsToReach
            .Should().BeGreaterThan(50);
    }

    [Fact]
    public void price_growth_uses_integer_monthly_compounding()
    {
        // 12.000 bps/năm không hợp lệ ở tầng nhập; dùng 1200 bps → 100 bps/tháng: 100.000.000 → 101.000.000 → 102.010.000 (làm tròn xuống từng tháng)
        var result = GoalSimulator.Simulate(100_000_000, 0, Income(1, 1, 1), 0, 0, 0, 1200, 2);

        result.PriceAtHorizonVnd.Should().Be(102_010_000);
    }

    [Fact]
    public void verdict_feasible_when_conservative_within_horizon()
    {
        // bảo thủ: thu Q10 24 − chi Q90 14 = 10 triệu/tháng → (600 − 100) / 10 = 50 tháng, trong horizon 60
        var result = GoalSimulator.Simulate(600_000_000, 100_000_000, Income(24_000_000, 25_000_000, 30_000_000), 8_000_000, 12_000_000, 14_000_000, 0, 60);

        result.Scenarios[0].MonthlyCapacityVnd.Should().Be(10_000_000);
        result.Scenarios[0].MonthsToReach.Should().Be(50);
        result.Verdict.Should().Be(GoalVerdict.Feasible);
    }

    [Fact]
    public void verdict_tight_when_only_base_within_horizon()
    {
        // bảo thủ: 14 − 12 = 2 triệu/tháng → 250 tháng; cơ sở: 20 − 10 = 10 triệu/tháng → 50 tháng; horizon 60
        var result = GoalSimulator.Simulate(600_000_000, 100_000_000, Income(14_000_000, 20_000_000, 26_000_000), 8_000_000, 10_000_000, 12_000_000, 0, 60);

        result.Scenarios[0].MonthsToReach.Should().Be(250);
        result.Scenarios[1].MonthsToReach.Should().Be(50);
        result.Verdict.Should().Be(GoalVerdict.Tight);
    }

    [Fact]
    public void verdict_infeasible_when_base_exceeds_horizon()
    {
        var result = GoalSimulator.Simulate(600_000_000, 100_000_000, Income(14_000_000, 20_000_000, 26_000_000), 8_000_000, 17_000_000, 19_000_000, 0, 24);

        result.Verdict.Should().Be(GoalVerdict.Infeasible);
        result.Scenarios[1].ShortfallVnd.Should().Be(600_000_000 - (100_000_000 + (3_000_000L * 24)));
    }

    [Fact]
    public void negative_capacity_means_never()
    {
        var result = GoalSimulator.Simulate(600_000_000, 100_000_000, Income(5_000_000, 8_000_000, 9_000_000), 6_000_000, 10_000_000, 12_000_000, 0, 24);

        result.Scenarios[0].MonthsToReach.Should().BeNull();
        result.Scenarios[1].MonthsToReach.Should().BeNull();
        result.Scenarios[1].MonthlyCapacityVnd.Should().BeNegative();
        result.Verdict.Should().Be(GoalVerdict.Infeasible);
    }

    [Fact]
    public void insufficient_history_gives_insufficient_data_and_no_scenarios()
    {
        var result = GoalSimulator.Simulate(600_000_000, 100_000_000, Income(1, 1, 1, months: 2), 0, 0, 0, 0, 24);

        result.Verdict.Should().Be(GoalVerdict.InsufficientData);
        result.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void search_is_bounded_so_a_tiny_surplus_does_not_loop_forever()
    {
        var result = GoalSimulator.Simulate(1_000_000_000_000, 0, Income(1, 1, 1), 0, 0, 0, 0, 24);

        result.Scenarios[1].MonthsToReach.Should().BeNull(); // 1 đồng/tháng không bao giờ tới trong AgentLimits.MaxSearchMonths
    }
}
