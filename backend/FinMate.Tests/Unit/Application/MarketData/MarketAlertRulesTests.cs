using FinMate.Application.Budgets;
using FinMate.Application.Forecasting;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

public class MarketAlertRulesTests
{
    [Theory]
    [InlineData(20000, 20600, 3, true)]
    [InlineData(20000, 20599, 3, false)]
    [InlineData(20000, 19000, 3, false)]
    [InlineData(20000, 20000, 3, false)]
    public void RiseIsMeasuredExactlyAtTheThreshold(long reference, long latest, int threshold, bool expected)
        => MarketAlertRules.RoseEnough(reference, latest, threshold).Should().Be(expected);

    [Fact]
    public void ExtraCostIsMonthlySpendTimesTheRiseRoundedToAThousand()
    {
        // 1.200.000đ trong 60 ngày là 600.000đ một tháng, tăng 5% là 30.000đ.
        MarketAlertRules.ExtraCostCents(1_200_000, 60, 20_000, 21_000).Should().Be(30_000);

        // 16.400đ làm tròn thành 16.000đ.
        MarketAlertRules.ExtraCostCents(656_000, 60, 20_000, 21_000).Should().Be(16_000);
    }

    [Theory]
    [InlineData("Petrolimex CHXD 12", null, true)]
    [InlineData("PVOIL", null, true)]
    [InlineData(null, "đổ xăng xe máy", true)]
    [InlineData("ĐỔ XĂNG", null, true)]
    [InlineData("Grab", "đi làm", false)]
    [InlineData("Xe buýt", null, false)]
    public void RecognisesFuelPurchases(string? merchant, string? description, bool expected)
        => MarketAlertRules.IsFuelPurchase(new Transaction { MerchantName = merchant, Description = description }).Should().Be(expected);

    private static BudgetForecastDto Budget(long spent, string status)
        => new(Guid.NewGuid(), Guid.NewGuid(), "Di chuyển", 1_000_000, spent, spent, 0, status, 0, 10, BudgetRiskBasis.Pace);

    [Theory]
    [InlineData(100_000, BudgetRiskStatus.Ok, false)]
    [InlineData(700_000, BudgetRiskStatus.Ok, true)]
    [InlineData(100_000, BudgetRiskStatus.AtRisk, true)]
    [InlineData(100_000, BudgetRiskStatus.Over, true)]
    public void ABudgetIsTightWhenForecastedOverOrPastT1(long spent, string status, bool expected)
        => MarketAlertRules.BudgetIsTight(Budget(spent, status), new AlertThresholds(70, 90)).Should().Be(expected);

    [Theory]
    [InlineData(1, true)] // 08:00 VN
    [InlineData(13, true)] // 20:00 VN
    [InlineData(14, false)] // 21:00 VN
    [InlineData(17, false)] // 00:00 VN
    [InlineData(0, false)] // 07:00 VN
    public void AlertsAtNightWaitForMorning(int utcHour, bool sendsNow)
    {
        var now = new DateTimeOffset(2026, 11, 6, utcHour, 0, 0, TimeSpan.Zero);

        var notBefore = MarketAlertRules.NotBefore(now);

        if (sendsNow)
        {
            notBefore.Should().BeNull();
        }
        else
        {
            notBefore.Should().NotBeNull();
            notBefore!.Value.ToOffset(TimeSpan.FromHours(7)).Hour.Should().Be(8);
            notBefore.Value.Should().BeAfter(now);
        }
    }

    [Fact]
    public void TheFuelMessageUsesTheUsersOwnNumbers()
    {
        var body = MarketAlertRules.FuelBody(FuelKeys.E10Ron95Level3, 1_000, 82, 15_000, "Di chuyển", 14);

        body.Should().Be("Giá xăng RON 95 tăng khoảng 1.000đ/lít so với 14 ngày trước. Ngân sách Di chuyển của bạn đã dùng 82%. Với mức đổ xăng như mọi tháng, bạn có thể tốn thêm khoảng 15.000đ. Gộp các chuyến đi gần nhau sẽ giúp bạn giữ trong hạn mức.");
    }

    [Fact]
    public void TheFuelMessageDropsTheEstimateWhenItRoundsToNothing()
        => MarketAlertRules.FuelBody(FuelKeys.E5Ron92Level2, 100, 90, 0, "Di chuyển", null).Should().NotContain("tốn thêm").And.Contain("mức giá trước đó");

    [Fact]
    public void OnlyMappedTopicsHaveATemplate()
    {
        MarketAlertRules.NewsTemplate(MarketTopic.Electricity).Should().NotBeNull();
        MarketAlertRules.NewsTemplate(MarketTopic.Other).Should().BeNull();
        MarketAlertRules.CategorySlug(MarketTopic.Water).Should().Be("bills");
        MarketAlertRules.CategorySlug(MarketTopic.Other).Should().BeNull();
    }
}
