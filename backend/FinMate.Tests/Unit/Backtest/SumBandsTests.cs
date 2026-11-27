using FinMate.Application.Forecasting;
using FinMate.Backtest.Diagnostics;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;
using FinMate.Application.Admin.SystemConfig;
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

        high.Should().BeApproximately(NetFlowBand.Z * Math.Sqrt(variance * 9 + variance / 4 * 81), 0.5, "nửa độ rộng làm tròn tới đồng");
        low.Should().Be(-high);
    }

    [Fact]
    public void B2_UsesTheWeekdayMean_SoAFixedWeeklyPatternHasNoWidth()
    {
        var week = new double[] { 0, -100, -100, -100, -100, -100, -500 };
        var series = Series([.. week, .. week, .. week]);

        SumBands.Normal(series, 14)!.Value.Should().Be((0.0, 0.0), "thứ nào cũng giống hệt nhau mỗi tuần: không còn bất định");
    }

    [Theory]
    [InlineData(Persona.StableWorker)]
    [InlineData(Persona.Freelancer)]
    [InlineData(Persona.Family)]
    public void TheEngineBand_IsB2RebuiltFromTheHarnessHistory(string persona)
    {
        // Task 9: engine dùng B2, nên dải dựng lại trong harness từ cùng lịch sử phải trùng từng đồng; không thì bảng C7 không còn đo đúng production.
        var settings = new ForecastSettings();
        var rules = ForecastRules.From(settings);
        var world = WorldBuilder.Build(persona, 1, ScenarioCodes.K0);
        var harness = new ForecastHarness(world, settings);
        var origin = new DateOnly(2025, 12, 10);
        var forecast = harness.ForecastAt(origin, 30);
        var items = new OracleRecurring(world).ItemsAt(harness.Clock.Now).Select(i => i.ToView()).ToList();
        var history = RefundNetting.ForHabits(harness.View.Raw(origin.AddDays(-rules.HistoryWindowDays), origin).Where(t => !t.IsLoan).ToList());
        var net = SumBands.NetSeries(history, items, origin, rules);

        foreach (var h in new[] { 7, 14, 30 })
        {
            SumBands.At(SumBands.B2, forecast, h, net).Should().Be(SumBands.At(SumBands.Current, forecast, h, net), $"{persona} h={h}");
        }
    }
}
