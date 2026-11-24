using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Application.MarketData;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

public class GoalMarketRulesTests
{
    private static readonly GoalMarketAlertSettings Settings = new();
    private static readonly DateOnly Today = new(2026, 11, 23);

    private static GoalForecast Forecast(string status, DateOnly? likely)
        => new(status, 1, 1, 1, 3, false, likely, likely, null);

    [Fact]
    public void TheAdjustedTargetScalesWithThePriceAndRoundsUp()
    {
        GoalMarketRules.AdjustedTarget(100_000_000, 135_000_000, 148_500_000).Should().Be(110_000_000);
        GoalMarketRules.AdjustedTarget(100_000_000, 135_000_000, 135_000_001).Should().Be(100_100_000);
        GoalMarketRules.AdjustedTarget(90_000_000_000, 135_000_000, 500_000_000).Should().BeGreaterThan(0, "không tràn số với mục tiêu lớn");
    }

    [Fact]
    public void ChangeIsInBasisPoints()
        => GoalMarketRules.ChangeBps(135_000_000, 148_500_000).Should().Be(1000);

    [Fact]
    public void ALongEnoughDelayIsReported()
        => GoalMarketRules.ShiftToReport(Forecast("no_deadline", Today.AddMonths(16)), Forecast("no_deadline", Today.AddMonths(18)), 1000, Settings).Should().BeInRange(59, 62);

    [Fact]
    public void ASmallDelayIsNotReported()
        => GoalMarketRules.ShiftToReport(Forecast("no_deadline", Today.AddDays(100)), Forecast("no_deadline", Today.AddDays(110)), 1000, Settings).Should().BeNull();

    [Fact]
    public void ASmallDelayThatMakesTheGoalLateIsReported()
        => GoalMarketRules.ShiftToReport(Forecast(GoalForecastStatus.OnTrack, Today.AddDays(100)), Forecast(GoalForecastStatus.Behind, Today.AddDays(110)), 1000, Settings).Should().Be(10);

    [Fact]
    public void ASmallRiseOrAGoalWithoutContributionsIsNotReported()
    {
        GoalMarketRules.ShiftToReport(Forecast("no_deadline", Today.AddMonths(16)), Forecast("no_deadline", Today.AddMonths(18)), 400, Settings).Should().BeNull();
        GoalMarketRules.ShiftToReport(Forecast(GoalForecastStatus.Unreachable, null), Forecast(GoalForecastStatus.Unreachable, null), 1000, Settings).Should().BeNull();
    }

    [Fact]
    public void TheAlertCarriesAnUpdateButtonOnlyWithItsGoal()
    {
        var data = JsonDocument.Parse("""{"goalId":"7c1b2a52-55a5-4fb1-9f2b-0a8f6e0b5a11","adjustedTargetCents":"110000000"}""").RootElement;

        var actions = ForecastAlertActions.For(ForecastAlertKind.GoalMarketShift, data);

        actions.Select(a => a.Kind).Should().Equal(ForecastAlertActionKinds.UpdateGoalTarget, ForecastAlertActionKinds.OpenGoal);
        actions[0].AmountCents.Should().Be(110_000_000);
        ForecastAlertActions.For(ForecastAlertKind.GoalMarketShift, JsonDocument.Parse("{}").RootElement).Should().BeEmpty();
    }

    [Fact]
    public void TheBodyHasNoBuyOrSellAdvice()
        => GoalMarketRules.Body(new GoalMarketTopic { Key = "gold", Label = "vàng" }, "Mua vàng cưới", 1000, 61, false, 110_000_000)
            .Should().Contain("lùi khoảng 3 tháng").And.NotContain("nên");
}
