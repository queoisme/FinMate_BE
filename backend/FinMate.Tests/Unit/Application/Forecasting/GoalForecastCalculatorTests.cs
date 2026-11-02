using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class GoalForecastCalculatorTests
{
    [Fact]
    public void AlreadySaved_IsReached()
        => GoalForecastCalculator.Compute(10_000_000, 10_000_000, null, [1_000_000], Today, null).Status.Should().Be(GoalForecastStatus.Reached);

    [Fact]
    public void NoContributions_IsUnreachable_AndSaysWhatTheDeadlineNeeds()
    {
        var deadline = Today.AddDays(183);   // ~6 tháng

        var forecast = GoalForecastCalculator.Compute(12_000_000, 0, deadline, [0, 0, 0], Today, null);

        forecast.Status.Should().Be(GoalForecastStatus.Unreachable);
        forecast.LikelyDate.Should().BeNull();
        forecast.RequiredMonthlyCents.Should().BeInRange(1_990_000, 2_000_000);
    }

    [Fact]
    public void AverageAndConservativeRates_GiveTwoDates()
    {
        // Góp 0, 2tr, 2tr, 4tr: trung bình 2tr; phân vị 25 = 1,5tr. Còn thiếu 6tr → ~3 tháng và ~4 tháng.
        var forecast = GoalForecastCalculator.Compute(10_000_000, 4_000_000, null, [0, 2_000_000, 2_000_000, 4_000_000], Today, null);

        forecast.Status.Should().Be(GoalForecastStatus.NoDeadline);
        forecast.AverageMonthlyCents.Should().Be(2_000_000);
        forecast.ConservativeMonthlyCents.Should().Be(1_500_000);
        forecast.LikelyDate.Should().Be(Today.AddDays(92));
        forecast.ConservativeDate.Should().Be(Today.AddDays(122));
    }

    [Fact]
    public void ConservativeDateBeforeTheDeadline_IsOnTrack()
    {
        var forecast = GoalForecastCalculator.Compute(10_000_000, 4_000_000, Today.AddDays(150), [2_000_000, 2_000_000], Today, null);

        forecast.Status.Should().Be(GoalForecastStatus.OnTrack);
        forecast.RequiredMonthlyCents.Should().BeNull();
    }

    [Fact]
    public void ConservativeDateAfterTheDeadline_IsBehind_WithTheMonthlyAmountNeeded()
    {
        // Còn 6tr, hạn ~2 tháng nữa (61 ngày ≈ 2,004 tháng) → cần ~2,99tr/tháng.
        var forecast = GoalForecastCalculator.Compute(10_000_000, 4_000_000, Today.AddDays(61), [1_000_000, 1_000_000], Today, null);

        forecast.Status.Should().Be(GoalForecastStatus.Behind);
        forecast.RequiredMonthlyCents.Should().Be(GoalForecastCalculator.RequiredMonthly(6_000_000, Today, Today.AddDays(61)));
        forecast.RequiredMonthlyCents.Should().BeInRange(2_990_000, 3_000_000);
    }

    [Fact]
    public void Scenario_ReplacesBothRates()
    {
        var forecast = GoalForecastCalculator.Compute(10_000_000, 4_000_000, Today.AddDays(61), [0], Today, 3_000_000);

        forecast.IsScenario.Should().BeTrue();
        forecast.AverageMonthlyCents.Should().Be(3_000_000);
        forecast.ConservativeMonthlyCents.Should().Be(3_000_000);
        forecast.Status.Should().Be(GoalForecastStatus.OnTrack);
    }

    [Fact]
    public void PastDeadline_NeedsTheWholeRemainderNow()
        => GoalForecastCalculator.RequiredMonthly(5_000_000, Today, Today.AddDays(-10)).Should().Be(5_000_000);
}
