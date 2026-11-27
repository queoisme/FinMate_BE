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

    /// <summary>Phase 97 (F1): mỗi thứ trong tuần 12 ngày chi 50.000đ và 1 ngày chi 2.000.000đ — đuôi nặng, trung bình lớn hơn P75.</summary>
    private static List<ForecastTransaction> HeavyTailed()
        => Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), i % 13 == 0 ? 2_000_000 : 50_000, "Quán ăn")).ToList();

    [Fact]
    public void Expected_IsTheMeanOfTheWeekday_NotTheMedian()
    {
        // Phase 97, Task 1: kỳ vọng của một TỔNG là tổng các trung bình; trung vị (có cắt ngoại lai) dự báo thấp hơn thực tế 24–43% với chi tiêu đuôi nặng.
        var baseline = SpendingBaselineCalculator.Compute(HeavyTailed(), [], 0, Today, Rules);

        for (var d = 0; d < 7; d++)
        {
            var samples = Enumerable.Range(1, 91).Where(i => (int)Today.AddDays(-i).DayOfWeek == d).Select(i => i % 13 == 0 ? 2_000_000.0 : 50_000.0).ToList();
            baseline.Expected[d].Should().Be((long)Math.Round(samples.Average(), MidpointRounding.AwayFromZero));
        }

        baseline.Expected.Should().OnlyContain(e => e > 50_000, "trung vị của mỗi thứ là 50.000đ; trung bình phải tính cả ngày lớn");
    }

    [Fact]
    public void HeavyTailedSpending_TheBandStillSurroundsTheMean_WithTheOldWidth()
    {
        // S1: tầng 1 giữ độ rộng cũ (P75 − trung vị, trung vị − P25 trên dữ liệu đã cắt ngoại lai) nhưng dời tâm theo đường dự kiến mới.
        var baseline = SpendingBaselineCalculator.Compute(HeavyTailed(), [], 0, Today, Rules);

        for (var d = 0; d < 7; d++)
        {
            baseline.Low[d].Should().BeLessThanOrEqualTo(baseline.Expected[d]);
            baseline.High[d].Should().BeGreaterThanOrEqualTo(baseline.Expected[d]);
            // Mọi thứ đều có trung vị = P25 = P75 = 50.000đ sau khi cắt ngày 2.000.000đ, nên độ rộng cũ là 0: dải trùng đường dự kiến, không dồn về một phía.
            baseline.High[d].Should().Be(baseline.Expected[d]);
            baseline.Low[d].Should().Be(baseline.Expected[d]);
        }
    }

    [Fact]
    public void TheBandKeepsTheOldWidthAroundTheMean()
    {
        // 0, 100.000, 200.000, 300.000đ lặp lại: không có ngoại lai, trung vị 150.000, P25 75.000, P75 225.000, trung bình 150.000.
        var history = Enumerable.Range(1, 84).Select(i => Debit(Today.AddDays(-i), i % 4 * 100_000, "Quán ăn")).Where(t => t.AmountCents > 0).ToList();
        history.Add(Debit(Today.AddDays(-84), 1, "mốc"));   // ngày đầu của chuỗi, để chuỗi đủ 84 ngày

        var baseline = SpendingBaselineCalculator.Compute(history, [], 0, Today, Rules);

        for (var d = 0; d < 7; d++)
        {
            (baseline.High[d] - baseline.Expected[d]).Should().BeGreaterThan(0);
            (baseline.Expected[d] - baseline.Low[d]).Should().BeGreaterThan(0);
        }
    }
}
