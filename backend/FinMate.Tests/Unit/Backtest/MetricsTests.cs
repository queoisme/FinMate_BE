using FinMate.Application.Forecasting;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Domain.Enums;
using FinMate.Tests.Unit.Application.Forecasting;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

public class MetricsTests
{
    private static readonly DateOnly Day = new(2025, 9, 1);

    [Fact]
    public void Naive_RepeatsTheFlowOfTheSameNumberOfDays_AndScalesWhenHistoryIsShort()
    {
        Baselines.Naive(1_000_000, -80_000, 8, 7).Should().Be(920_000);
        Baselines.Naive(1_000_000, -40_000, 4, 7).Should().Be(920_000, "4 ngày có dữ liệu được nhân lên 8 ngày");
        Baselines.Naive(1_000_000, 123, 0, 7).Should().Be(1_000_000);
        Baselines.NaiveWindow(Day, Day.AddDays(-3), 7).Should().Be((Day.AddDays(-3), 3));
        Baselines.NaiveWindow(Day, Day.AddDays(-100), 7).Should().Be((Day.AddDays(-8), 8));
    }

    [Fact]
    public void MeanDailyFlexible_UsesTheBaselineFilters()
    {
        var rules = ForecastTestData.Rules;
        var rent = ForecastTestData.Item("Tiền nhà", 3_000_000, Day.AddDays(3));
        var habits = new List<ForecastTransaction>
        {
            ForecastTestData.Debit(Day.AddDays(-1), 300_000, "Quán cơm"),
            ForecastTestData.Debit(Day.AddDays(-2), 3_000_000, "Tiền nhà"),           // khớp khoản định kỳ: bỏ
            ForecastTestData.Debit(Day.AddDays(-3), 999_000, "Lẻ", excluded: true),   // bị loại khỏi dự báo: bỏ
            ForecastTestData.Credit(Day.AddDays(-4), 5_000_000, "Lương"),             // thu: bỏ
            ForecastTestData.Debit(Day.AddDays(-40), 7_000_000, "Cũ"),                // ngoài cửa sổ: bỏ
            ForecastTestData.Debit(Day, 50_000, "Hôm nay"),                           // không trước mốc: bỏ
        };

        Baselines.MeanDailyFlexible(habits, [rent], rules, Day, Day.AddDays(-365), 30).Should().Be(10_000);
        Baselines.MeanDailyFlexible(habits, [rent], rules, Day, Day.AddDays(-10), 30).Should().Be(30_000, "người mới: chia cho 10 ngày đã có");
        Baselines.Mean(1_000_000, -200_000, 10_000, 7).Should().Be(720_000);
    }

    [Fact]
    public void ExpectedEvents_SkipOptimisticOnlyEvents_AndStopAtTheHorizon()
    {
        var days = new List<CashflowDayDto>
        {
            new(Day, 0, 0, 0, 10, [new CashflowEventDto("recurring_outflow", "Nhà", -100, null)]),
            new(Day.AddDays(1), 0, 0, 0, 20, [new CashflowEventDto("loan_inflow", "Thu", 500, null, OptimisticOnly: true)]),
            new(Day.AddDays(2), 0, 0, 0, 30, [new CashflowEventDto("recurring_inflow", "Lương", 1_000, null)]),
        };
        var forecast = new CashflowForecastDto(Day, Day.AddDays(2), 0, 0, 0, 0, Day, 0, "normal", "history", days, []);

        Baselines.ExpectedEvents(forecast, 1).Should().Be(-100);
        Baselines.ExpectedEvents(forecast, 2).Should().Be(900);
        Baselines.ModelFlexible(forecast, 1).Should().Be(30);
    }

    [Fact]
    public void Balance_MaeBiasMedianAndImprovementOverHold_AreComputedByHand()
    {
        // Người 1: thực tế 1.000, mô hình 1.100 (sai +100), giữ nguyên 1.400 (sai +400). Người 2: thực tế 2.000, mô hình 1.700 (−300), giữ nguyên 2.100 (+100).
        var rows = new[]
        {
            Row(user: 1, actual: 1_000, start: 1_400, expected: 1_100, low: 900, high: 1_200),
            Row(user: 2, actual: 2_000, start: 2_100, expected: 1_700, low: 1_600, high: 1_800),
        };

        var result = MetricsBook.Compute(rows);
        var all = result.Balance.Where(b => b.Key.Persona == "all" && b.Key.Confidence == "all").ToList();
        var model = all.Single(b => b.Method == Methods.Model);
        var hold = all.Single(b => b.Method == Methods.Hold);

        model.Mae.Should().Be(200);
        model.Bias.Should().Be(-100);
        model.MedianAe.Should().Be(200);
        hold.Mae.Should().Be(250);
        hold.ImprovementPct.Should().Be(20.0, "1 − 400/500");
        hold.CiLow.Should().BeLessThanOrEqualTo(20.0);
        hold.CiHigh.Should().BeGreaterThanOrEqualTo(20.0);

        var band = result.Band.Single(b => b.Key.Persona == "all" && b.Key.Confidence == "all");
        band.CoveragePct.Should().Be(50.0, "người 1 nằm trong dải, người 2 thì không");
        band.MeanWidth.Should().Be(250);
    }

    [Fact]
    public void Flexible_WapeBiasAndMape_AreComputedByHand()
    {
        var rows = new[]
        {
            Row(user: 1, flexActual: 1_000_000, flexModel: 900_000),
            Row(user: 2, flexActual: 3_000_000, flexModel: 3_300_000),
            Row(user: 3, flexActual: 50_000, flexModel: 0),   // dưới sàn MAPE
        };

        var model = MetricsBook.Compute(rows).Flexible.Single(f => f.Key.Persona == "all" && f.Key.Confidence == "all" && f.Method == Methods.Model);

        model.WapePct.Should().Be(Math.Round(450_000 * 100.0 / 4_050_000, 1));
        model.BiasPct.Should().Be(Math.Round(150_000 * 100.0 / 4_050_000, 1));
        model.MapePct.Should().Be(10.0);
        model.MapeN.Should().Be(2);
        model.MapeExcluded.Should().Be(1);
    }

    [Fact]
    public void Income_WapeAndBias_AreComputedByHand_AndGroupsWithoutIncomeAreSkipped()
    {
        var rows = new[]
        {
            Row(user: 1) with { IncomeActual = 1_000_000, IncomeModel = 800_000 },
            Row(user: 2) with { IncomeActual = 3_000_000, IncomeModel = 3_600_000 },
            Row(user: 3, scenario: ScenarioCodes.K1),   // nhóm K1 không có thu: không có dòng
        };

        var income = MetricsBook.Compute(rows).Income.Where(i => i.Key.Persona == "all" && i.Key.Confidence == "all").ToList();

        income.Should().ContainSingle();
        income[0].WapePct.Should().Be(20.0, "(200.000 + 600.000) / 4.000.000");
        income[0].BiasPct.Should().Be(10.0, "(−200.000 + 600.000) / 4.000.000");
    }

    [Fact]
    public void ScenarioEffect_ComparesTheSameUserOriginAndHorizonAgainstK0()
    {
        var rows = new[]
        {
            Row(user: 1, actual: 1_000, expected: 1_100),
            Row(user: 1, actual: 1_000, expected: 1_300, scenario: ScenarioCodes.K1, affected: true),
            Row(user: 1, actual: 1_000, expected: 900, scenario: ScenarioCodes.K1, affected: false, origin: Day.AddDays(1)),
        };

        var effect = MetricsBook.Compute(rows).Effects.Single();

        effect.Scenario.Should().Be(ScenarioCodes.K1);
        effect.Control.Should().Be(ScenarioCodes.K0);
        effect.N.Should().Be(1);
        effect.MaeScenario.Should().Be(300);
        effect.MaeControl.Should().Be(100);
        effect.ChangePct.Should().Be(200.0);
    }

    [Fact]
    public void Bootstrap_IsDeterministic_AndCollapsesWhenEveryUserIsTheSame()
    {
        var same = Enumerable.Repeat((Num: 3.0, Den: 4.0), 30).ToList();
        Bootstrap.Interval(same, "k").Should().Be((0.75, 0.75));

        var mixed = Enumerable.Range(1, 40).Select(i => ((double)i, 10.0)).ToList();
        Bootstrap.Interval(mixed, "k").Should().Be(Bootstrap.Interval(mixed, "k"));
        Bootstrap.Interval([], "k").Should().BeNull();
    }

    private static OriginRow Row(
        int user, long actual = 0, long start = 0, long expected = 0, long low = 0, long high = 0, long flexActual = 0, long flexModel = 0,
        string scenario = ScenarioCodes.K0, bool affected = false, DateOnly? origin = null)
        => new(Modes.Oracle, scenario, Persona.StableWorker, user, origin ?? Day, 7, false, affected, actual, start, expected, low, high,
            0, 0, 0, flexActual, flexModel, 0, 0);
}
