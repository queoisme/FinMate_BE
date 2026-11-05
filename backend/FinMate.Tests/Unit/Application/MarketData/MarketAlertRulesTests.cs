using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Budgets;
using FinMate.Application.Forecasting;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
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

    private static MarketCostType Type(string key) => new MarketDataSettings().CostTypes.Single(t => t.Key == key);

    private static Transaction Spend(string? merchant, string? description = null, string? categoryName = null, string? slug = null, long amount = 100_000, int daysAgo = 3) => new()
    {
        MerchantName = merchant,
        Description = description,
        AmountCents = amount,
        TransactedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        CategoryId = categoryName is null ? null : Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Category = categoryName is null ? null : new Category { Name = categoryName, Slug = slug ?? "tu-tao" },
    };

    [Theory]
    [InlineData("Petrolimex CHXD 12", null, true)]
    [InlineData("PVOIL", null, true)]
    [InlineData(null, "đổ xăng xe máy", true)]
    [InlineData("ĐỔ XĂNG", null, true)]
    [InlineData("DO XANG QUAN 7", null, true)]
    [InlineData("Grab", "đi làm", false)]
    [InlineData("Xe buýt", null, false)]
    public void RecognisesFuelPurchases(string? merchant, string? description, bool expected)
        => MarketSpendProfile.Matches(Spend(merchant, description), Type("fuel")).Should().Be(expected);

    [Theory]
    [InlineData("Viettel Internet", "electricity", false)]
    [InlineData("EVN HCMC", "electricity", true)]
    [InlineData("Seven Coffee", "electricity", false)]
    [InlineData("THANH TOAN TIEN DIEN", "electricity", true)]
    [InlineData("SAWACO", "water", true)]
    public void RecognisesOtherCosts(string merchant, string key, bool expected)
        => MarketSpendProfile.Matches(Spend(merchant), Type(key)).Should().Be(expected);

    [Fact]
    public void ACustomCategoryNameCountsAndFoodMatchesItsSystemCategory()
    {
        MarketSpendProfile.Matches(Spend("Cửa hàng A", categoryName: "Tiền điện"), Type("electricity")).Should().BeTrue();
        MarketSpendProfile.Matches(Spend("Quán B", categoryName: "Ăn uống", slug: "food"), Type("food_price")).Should().BeTrue();
    }

    [Fact]
    public void TheProfileDescribesTheHabit()
    {
        var debits = new[]
        {
            Spend("Petrolimex", amount: 300_000, categoryName: "Đi lại", daysAgo: 2),
            Spend("Petrolimex", amount: 300_000, categoryName: "Đi lại", daysAgo: 20),
            Spend("Quán ăn", amount: 1_400_000),
        };

        var profile = MarketSpendProfile.Build(debits, Type("fuel"), 60)!;

        profile.Count.Should().Be(2);
        profile.MonthlyCents.Should().Be(300_000);
        profile.TimesPerMonth.Should().Be(1);
        profile.SharePercent.Should().Be(30);
        profile.MainCategoryId.Should().NotBeNull();
        profile.IsRelevant(new MarketDataSettings(), DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void SomeoneWhoStoppedOrBarelySpendsIsNotRelevant()
    {
        var settings = new MarketDataSettings();
        var old = new[] { Spend("Petrolimex", daysAgo: 50), Spend("Petrolimex", daysAgo: 55) };
        var tiny = new[] { Spend("Petrolimex", amount: 1_000), Spend("Petrolimex", amount: 1_000), Spend("Thuê nhà", amount: 8_000_000) };

        MarketSpendProfile.Build(old, Type("fuel"), 60)!.IsRelevant(settings, DateTimeOffset.UtcNow).Should().BeFalse("đã lâu không đổ xăng");
        MarketSpendProfile.Build(tiny, Type("fuel"), 60)!.IsRelevant(settings, DateTimeOffset.UtcNow).Should().BeFalse("khoản này không đáng kể");
        MarketSpendProfile.Build([Spend("Xe buýt")], Type("fuel"), 60).Should().BeNull();
    }

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
    public void WordingComesFromTheConfiguredLabel()
    {
        MarketAlertRules.Title(Type("electricity")).Should().Be("Giá điện đang tăng");
        MarketAlertRules.NewsBody(Type("water"), 85, "Sinh hoạt", "Báo Mẫu").Should().Be("Có tin giá nước tăng. Ngân sách Sinh hoạt của bạn đã dùng 85%, bạn nên để dư thêm cho kỳ tới. Nguồn: Báo Mẫu.");
    }
}
