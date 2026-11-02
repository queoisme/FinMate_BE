using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class SpendingBaselineCalculatorTests
{
    /// <summary>60 ngày liền, mỗi ngày chi <paramref name="perDay"/> — đủ dữ liệu theo mặc định (≥ 30 ngày).</summary>
    private static List<ForecastTransaction> DailySpending(long perDay, int days = 60)
        => Enumerable.Range(1, days).Select(i => Debit(Today.AddDays(-i), perDay, "Quán ăn")).ToList();

    [Fact]
    public void EnoughHistory_UsesTheTypicalDailySpend()
    {
        var baseline = SpendingBaselineCalculator.Compute(DailySpending(100_000), [], 0, Today, Rules);

        baseline.Confidence.Should().Be(ForecastConfidence.Normal);
        baseline.Basis.Should().Be(SpendingBaseline.BasisHistory);
        baseline.ExpectedOn(Today).Should().Be(100_000);
    }

    [Fact]
    public void ChargesOfATrackedRecurringItem_AreNotCountedTwice()
    {
        // Nhánh "trung bình" không cắt ngoại lai, nên khoản tiền nhà lọt vào là thấy ngay trong con số.
        var history = DailySpending(100_000, days: 10);
        history.Add(Debit(Today.AddDays(-5), 4_000_000, "Tiền nhà"));
        var rent = Item("Tiền nhà", 4_000_000, Today.AddDays(25));

        var withItem = SpendingBaselineCalculator.Compute(history, [rent], 0, Today, Rules);
        var without = SpendingBaselineCalculator.Compute(history, [], 0, Today, Rules);

        withItem.ExpectedOn(Today).Should().Be(100_000, "tiền nhà đã được engine đặt riêng vào ngày đến hạn");
        without.ExpectedOn(Today).Should().Be(500_000);
    }

    [Fact]
    public void TransactionsExcludedByTheUser_AreIgnored()
    {
        var history = DailySpending(100_000);
        history.AddRange(Enumerable.Range(1, 60).Select(i => Debit(Today.AddDays(-i), 900_000, "Tiệc", excluded: true)));

        SpendingBaselineCalculator.Compute(history, [], 0, Today, Rules).ExpectedOn(Today).Should().Be(100_000);
    }

    [Fact]
    public void ThinHistory_FallsBackToTheBudget_WithLowConfidence()
    {
        var baseline = SpendingBaselineCalculator.Compute(DailySpending(100_000, days: 10), [], 9_000_000, Today, Rules);

        baseline.Basis.Should().Be(SpendingBaseline.BasisBudget);
        baseline.Confidence.Should().Be(ForecastConfidence.Low);
        baseline.ExpectedOn(Today).Should().Be(9_000_000 / 30);
    }

    [Fact]
    public void ThinHistoryAndNoBudget_FallsBackToTheAverageSoFar()
    {
        var baseline = SpendingBaselineCalculator.Compute(DailySpending(100_000, days: 10), [], 0, Today, Rules);

        baseline.Basis.Should().Be(SpendingBaseline.BasisAverage);
        baseline.ExpectedOn(Today).Should().Be(100_000);
    }

    [Fact]
    public void NoDataAtAll_ForecastsNoFlexibleSpending()
        => SpendingBaselineCalculator.Compute([], [], 0, Today, Rules).Basis.Should().Be(SpendingBaseline.BasisNone);

    [Fact]
    public void Band_IsOrderedLowExpectedHigh()
    {
        var history = Enumerable.Range(1, 60).Select(i => Debit(Today.AddDays(-i), 50_000 + (i % 5 * 20_000), "Quán ăn")).ToList();

        var baseline = SpendingBaselineCalculator.Compute(history, [], 0, Today, Rules);

        for (var d = 0; d < 7; d++)
        {
            baseline.Low[d].Should().BeLessThanOrEqualTo(baseline.Expected[d]);
            baseline.Expected[d].Should().BeLessThanOrEqualTo(baseline.High[d]);
        }
    }
}
