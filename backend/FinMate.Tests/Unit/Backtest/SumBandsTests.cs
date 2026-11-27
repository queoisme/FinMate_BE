using FinMate.Backtest.Diagnostics;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

/// <summary>Phase 97, Task 8: hai cách dựng dải từ tổng h+1 ngày (B1, B2) trên chuỗi biết trước.</summary>
public class SumBandsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static List<(DateOnly Date, double Amount)> Series(params double[] values)
        => values.Select((v, i) => (Monday.AddDays(i), v)).ToList();

    [Fact]
    public void B1_IsTheQuartilesOfRollingSums_AroundTheMeanTimesLength()
    {
        // Tổng trượt 2 ngày của 0,10,0,10,0 là 10,10,10,10: không dao động, trung bình × 2 = 8 → dải lệch hẳn lên +2.
        SumBands.RollingQuantiles(Series(0, 10, 0, 10, 0), 2).Should().Be((2.0, 2.0));
        SumBands.RollingQuantiles(Series(1, 2), 3).Should().BeNull("lịch sử ngắn hơn L");
    }

    [Fact]
    public void B2_GrowsWithTheSquareRootOfLength_PlusTheErrorOfTheEstimatedMean()
    {
        // Mỗi thứ dưới 3 mẫu nên mức = trung bình chung 5; phần dư ±5, phương sai mẫu = 25·4/3.
        var series = Series(0, 10, 0, 10);
        var variance = 100.0 / 3;
        var (low, high) = SumBands.Normal(series, 9)!.Value;

        high.Should().BeApproximately(SumBands.Z * Math.Sqrt(variance * 9 + variance / 4 * 81), 1e-9);
        low.Should().Be(-high);
    }

    [Fact]
    public void B2_UsesTheWeekdayMean_SoAFixedWeeklyPatternHasNoWidth()
    {
        var week = new double[] { 0, -100, -100, -100, -100, -100, -500 };
        var series = Series([.. week, .. week, .. week]);

        SumBands.Normal(series, 14)!.Value.Should().Be((0.0, 0.0), "thứ nào cũng giống hệt nhau mỗi tuần: không còn bất định");
    }
}
