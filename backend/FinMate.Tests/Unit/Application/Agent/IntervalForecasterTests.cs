using FinMate.Application.Agent;
using FinMate.Application.Reports;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

public class IntervalForecasterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly long[] Freelancer = [5_000_000, 20_000_000, 8_000_000, 30_000_000, 2_000_000, 15_000_000];

    [Fact]
    public void fewer_than_three_months_is_insufficient()
    {
        var profile = IntervalForecaster.BuildProfile([10_000_000, 12_000_000]);

        profile.Segment.Should().Be(IncomeSegment.Unknown);
        profile.MonthsOfHistory.Should().Be(2);
    }

    [Fact]
    public void constant_salary_collapses_interval()
    {
        var profile = IntervalForecaster.BuildProfile(Enumerable.Repeat(15_000_000L, 6).ToArray());

        profile.Q10.Should().Be(15_000_000);
        profile.Q50.Should().Be(15_000_000);
        profile.Q90.Should().Be(15_000_000);
        profile.Cv.Should().Be(0);
        profile.Segment.Should().Be(IncomeSegment.Stable);
    }

    [Fact]
    public void freelancer_has_wide_interval()
    {
        var profile = IntervalForecaster.BuildProfile(Freelancer);

        profile.Q10.Should().Be(3_500_000);   // sắp xếp 2,5,8,15,20,30 triệu; nội suy tuyến tính tại vị trí 0,5
        profile.Q50.Should().Be(11_500_000);
        profile.Q90.Should().Be(25_000_000);
        profile.Segment.Should().Be(IncomeSegment.Variable);
        profile.MonthsOfHistory.Should().Be(6);
    }

    [Fact]
    public void override_wins_over_computed_segment()
    {
        IntervalForecaster.BuildProfile(Enumerable.Repeat(15_000_000L, 6).ToArray(), IncomeSegment.Variable)
            .Segment.Should().Be(IncomeSegment.Variable);
        IntervalForecaster.BuildProfile(Freelancer, IncomeSegment.Stable).Segment.Should().Be(IncomeSegment.Stable);
    }

    [Fact]
    public void segment_threshold_is_the_single_constant()
    {
        // 5 tháng 10 triệu + 1 tháng 14 triệu: CV khoảng 0,13 < 0,25 → ổn định; đổi hằng số thì kết luận đổi theo, không có số thứ hai ở nơi khác.
        var profile = IntervalForecaster.BuildProfile([10_000_000, 10_000_000, 10_000_000, 10_000_000, 10_000_000, 14_000_000]);
        profile.Cv.Should().BeLessThan(AgentLimits.VariableSegmentCv);
        profile.Segment.Should().Be(IncomeSegment.Stable);
    }

    [Fact]
    public async Task income_history_reads_through_the_data_source_and_applies_the_override()
    {
        var source = new InMemoryFinancialDataSource(Freelancer, [9_000_000, 9_000_000, 9_000_000, 9_000_000, 9_000_000, 9_000_000], 50_000_000, IncomeSegment.Stable);

        var profile = await new IncomeHistory(source).GetAsync(Guid.NewGuid(), Now);

        profile.Segment.Should().Be(IncomeSegment.Stable);
        profile.Q50.Should().Be(11_500_000);
    }
}

public class StatisticsQuantileTests
{
    [Theory]
    [InlineData(0.0, 10)]
    [InlineData(0.5, 25)]   // vị trí 1,5 giữa 20 và 30
    [InlineData(1.0, 40)]
    [InlineData(0.9, 37)]   // vị trí 2,7: 30 + 10*0,7 = 37
    public void quantile_interpolates_linearly_in_integers(double p, long expected)
        => Statistics.Quantile([10, 20, 30, 40], p).Should().Be(expected);

    [Fact]
    public void quantile_of_one_value_is_that_value()
        => Statistics.Quantile([7], 0.9).Should().Be(7);

    [Fact]
    public void quantile_of_nothing_is_zero()
        => Statistics.Quantile([], 0.5).Should().Be(0);

    [Fact]
    public void coefficient_of_variation_is_zero_for_constant_and_for_zero_mean()
    {
        Statistics.CoefficientOfVariation([10, 10, 10, 10]).Should().Be(0);
        Statistics.CoefficientOfVariation([0, 0, 0]).Should().Be(0);
    }

    [Fact]
    public void coefficient_of_variation_grows_with_spread()
        => Statistics.CoefficientOfVariation([5, 20, 8, 30, 2, 15])
            .Should().BeGreaterThan(Statistics.CoefficientOfVariation([14, 15, 16, 15, 14, 16]));
}
