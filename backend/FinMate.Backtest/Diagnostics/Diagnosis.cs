using System.Globalization;
using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Reporting;
using FinMate.Backtest.Simulation;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Diagnostics;

/// <summary>
/// Chẩn đoán sau tầng 1 (Phase 96, task chẩn đoán): tách sai số theo thành phần (H1), ba cách tính chi linh hoạt (H2), truy vết một ca,
/// K5h so với K5, dải trên nhóm thu nhập đều. Chỉ đo trong harness; không đổi production, bộ sinh hay báo cáo đã sinh. Kết quả là văn bản
/// Markdown để chép vào kế hoạch.
/// </summary>
public static partial class Diagnosis
{
    private static readonly int[] Horizons = [7, 14, 30];
    private static readonly string[] Regular = [Persona.StableWorker, Persona.Family];

    public static string Run(RunOptions options, Action<string>? progress = null)
    {
        var md = new StringBuilder();
        md.Append($"Chẩn đoán: hạt gốc {options.RootSeed}, {options.SeedsPerPersona} hạt giống mỗi hồ sơ, bước mốc {options.StepDays} ngày, K0, chế độ đáp án.\n\n");
        var acc = new Accumulator();
        foreach (var persona in options.Personas)
        {
            for (var seed = 0; seed < options.SeedsPerPersona; seed++)
            {
                Collect(WorldBuilder.Build(persona, seed, ScenarioCodes.K0, options.RootSeed), options, acc);
            }

            progress?.Invoke($"chẩn đoán K0: {persona}");
        }

        WriteDecomposition(md, acc, options.Personas);
        WriteFlexVariants(md, acc, options.Personas);
        WriteBands(md, acc);
        WriteTrace(md, options);
        WriteHoliday(md, options, progress);
        WriteEstimators(md, acc, options.Personas);
        return md.ToString();
    }

    private static void Collect(SynthWorld world, RunOptions options, Accumulator acc)
    {
        var rules = ForecastRules.From(options.Settings);
        var harness = new ForecastHarness(world, options.Settings, options.ConfigVersion);
        var oracle = new OracleRecurring(world);
        var truth = world.Transactions.ToDictionary(t => t.Id);
        foreach (var origin in BacktestRunner.Origins(world, options.StepDays))
        {
            var forecast = harness.ForecastAt(origin, Horizons.Max());
            var items = oracle.ItemsAt(harness.Clock.Now).Select(i => i.ToView()).ToList();
            var history = RefundNetting.ForHabits(harness.View.Raw(origin.AddDays(-rules.HistoryWindowDays), origin).Where(t => !t.IsLoan).ToList());
            var levels = FlexVariants.Compute(history, items, origin, rules);
            var flexLevels = Estimators.Levels(history, items, origin, rules, TransactionType.Debit);
            var incomeLevels = Estimators.Levels(history, items, origin, rules, TransactionType.Credit);
            foreach (var h in Horizons)
            {
                var to = origin.AddDays(h);
                var actual = Components.Actual(harness.TruthRaw(origin, to), harness.TruthBoundary(origin, to), truth);
                var predicted = Components.Predicted(forecast, h);
                var actualBalance = harness.ActualBalance(forecast.StartBalanceCents, origin, to);
                acc.AddDecomposition(world.Persona, h, predicted, actual, forecast.Days[h].ExpectedBalanceCents - actualBalance);
                var flexActual = FlexActual(harness, truth, origin, to);
                acc.AddFlex(world.Persona, h, levels, origin, flexActual, Baselines.ModelFlexible(forecast, h));
                acc.AddBand(world.Persona, h, forecast, levels, flexActual, actualBalance);
                AddEstimates(acc, world.Persona, h, origin, flexLevels, incomeLevels, flexActual, actual[Component.IncomeIrregular], Baselines.ModelFlexible(forecast, h));
            }
        }
    }

    private static void AddEstimates(
        Accumulator acc, string persona, int h, DateOnly origin, IReadOnlyDictionary<string, long[]>? flex, IReadOnlyDictionary<string, long[]>? income,
        long flexActual, long incomeActual, long productionFlex)
    {
        acc.AddEstimate(persona, h, EstimatorKinds.Flex, EstimatorKinds.Production, productionFlex, flexActual);
        acc.AddEstimate(persona, h, EstimatorKinds.Income, EstimatorKinds.Production, 0, incomeActual);   // production không chiếu thu không định kỳ (H1)
        foreach (var method in Estimators.All)
        {
            acc.AddEstimate(persona, h, EstimatorKinds.Flex, method, flex is null ? productionFlex : FlexVariants.Over(flex[method], origin, h), flexActual);
            acc.AddEstimate(persona, h, EstimatorKinds.Income, method, income is null ? 0 : FlexVariants.Over(income[method], origin, h), incomeActual);
        }
    }

    /// <summary>Đúng định nghĩa chi linh hoạt thực tế của báo cáo (đặc tả §7).</summary>
    private static long FlexActual(ForecastHarness harness, IReadOnlyDictionary<Guid, SynthTransaction> truth, DateOnly from, DateOnly to)
        => RefundNetting.ForHabits(harness.TruthRaw(from, to).Where(t => !t.IsLoan).ToList())
            .Where(t => t.Type == TransactionType.Debit && truth[t.Id].Label != TxLabel.Recurring)
            .Sum(t => t.AmountCents);

    private static string Money(long cents) => Format.Money(cents);

    private static string Money(double cents) => Format.Money((long)Math.Round(cents, MidpointRounding.AwayFromZero));

    private static string Pct(double value) => Format.Pct(Math.Round(value * 100, 1, MidpointRounding.AwayFromZero));

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static ForecastSettings WithTet(ForecastSettings settings) => BacktestRunner.SettingsFor(ScenarioCodes.K5h, settings);
}
