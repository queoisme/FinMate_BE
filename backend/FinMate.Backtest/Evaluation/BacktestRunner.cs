using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Evaluation;

/// <param name="StepDays">Khoảng cách giữa hai mốc (D18: 1 ngày, chậm quá thì 3).</param>
public sealed record RunOptions(
    ulong RootSeed,
    int SeedsPerPersona,
    int StepDays,
    IReadOnlyList<string> Personas,
    IReadOnlyList<string> Scenarios,
    ForecastSettings Settings,
    int ConfigVersion)
{
    public static RunOptions Default => new(
        GeneratorSpec.DefaultRootSeed, GeneratorSpec.SeedsPerPersona, 1, GeneratorSpec.Personas, ScenarioCodes.Tier1, new ForecastSettings(), 0);
}

/// <summary>
/// Chạy backtest tầng 1 (chế độ đáp án): mỗi người gốc × kịch bản × mốc gọi <see cref="ForecastService"/> thật, rồi chấm số dư ở tầm 7/14/30 và chi linh hoạt.
/// Tuần tự, một luồng (D18), để kết quả tất định.
/// </summary>
public static class BacktestRunner
{
    public static readonly IReadOnlyList<int> Horizons = ForecastSnapshotBuilder.Horizons;
    private const int K6FirstDay = 14;
    private const int K6LastDay = 75;

    public static IReadOnlyList<OriginRow> Run(RunOptions options, Action<string>? progress = null)
    {
        var rows = new List<OriginRow>();
        for (var p = 0; p < options.Personas.Count; p++)
        {
            for (var seed = 0; seed < options.SeedsPerPersona; seed++)
            {
                foreach (var scenario in options.Scenarios)
                {
                    var world = WorldBuilder.Build(options.Personas[p], seed, scenario, options.RootSeed);
                    rows.AddRange(RunWorld(world, (p * 1000) + seed, options));
                }

                progress?.Invoke($"{options.Personas[p]} #{seed}");
            }
        }

        return rows;
    }

    public static IEnumerable<DateOnly> Origins(SynthWorld world, int stepDays)
    {
        var (from, to) = world.Scenario == ScenarioCodes.K6
            ? (Max(world.FirstDay.AddDays(K6FirstDay), GeneratorSpec.EvaluationStart), Min(world.FirstDay.AddDays(K6LastDay), GeneratorSpec.EvaluationEnd))
            : (GeneratorSpec.EvaluationStart, GeneratorSpec.EvaluationEnd);
        for (var d = from; d <= to; d = d.AddDays(stepDays))
        {
            yield return d;
        }
    }

    /// <summary>Cấu hình của mô hình cho kịch bản: K5h thêm khoảng Tết vào <c>forecast.holidays</c> (D17); mọi kịch bản khác dùng nguyên cấu hình.</summary>
    public static ForecastSettings SettingsFor(string scenario, ForecastSettings settings)
        => scenario != ScenarioCodes.K5h
            ? settings
            : settings with
            {
                Holidays =
                [
                    .. settings.Holidays,
                    new ForecastHoliday
                    {
                        Name = "Tết Bính Ngọ (backtest K5h)", Start = GeneratorSpec.HolidayConfigStart, End = GeneratorSpec.HolidayConfigEnd,
                        MultiplierPercent = GeneratorSpec.HolidayConfigMultiplierPercent,
                    },
                ],
            };

    private static IEnumerable<OriginRow> RunWorld(SynthWorld world, int user, RunOptions options)
    {
        var settings = SettingsFor(world.Scenario, options.Settings);
        var rules = ForecastRules.From(settings);
        var harness = new ForecastHarness(world, settings, options.ConfigVersion);
        var oracle = new OracleRecurring(world);
        var labels = world.Transactions.ToDictionary(t => t.Id, t => t.Label);
        var horizon = Horizons.Max();
        foreach (var origin in Origins(world, options.StepDays))
        {
            var forecast = harness.ForecastAt(origin, horizon);
            var items = oracle.ItemsAt(harness.Clock.Now).Select(i => i.ToView()).ToList();
            var habits = RefundNetting.ForHabits(harness.View.Raw(origin.AddDays(-GeneratorSpec.MeanBaselineDays), origin).Where(t => !t.IsLoan).ToList());
            var meanDaily = Baselines.MeanDailyFlexible(habits, items, rules, origin, world.FirstDay, GeneratorSpec.MeanBaselineDays);
            var snapshots = ForecastSnapshotBuilder.Build(world.UserId, forecast, options.ConfigVersion, harness.Clock.Now).ToDictionary(s => s.HorizonDays);
            foreach (var h in Horizons)
            {
                var day = forecast.Days[h];
                var (naiveFrom, naiveDays) = Baselines.NaiveWindow(origin, world.FirstDay, h);
                var naiveFlex = naiveDays == 0 ? 0 : FlexibleActual(harness, labels, naiveFrom, origin.AddDays(-1));
                yield return new OriginRow(
                    Modes.Oracle, world.Scenario, world.Persona, user, origin, h, forecast.Confidence == "low", IsAffected(world, origin, h),
                    harness.ActualBalance(forecast.StartBalanceCents, origin, origin.AddDays(h)), forecast.StartBalanceCents,
                    day.ExpectedBalanceCents, day.LowBalanceCents, day.HighBalanceCents, snapshots[h].RecurringOnlyCents,
                    Baselines.Mean(forecast.StartBalanceCents, Baselines.ExpectedEvents(forecast, h), meanDaily, h),
                    Baselines.Naive(forecast.StartBalanceCents, naiveDays == 0 ? 0 : harness.PastNetFlow(naiveFrom, origin.AddDays(-1)), naiveDays, h),
                    FlexibleActual(harness, labels, origin, origin.AddDays(h)), Baselines.ModelFlexible(forecast, h),
                    Baselines.Round(meanDaily * (h + 1)), naiveDays == 0 ? 0 : Baselines.Round((double)naiveFlex * (h + 1) / naiveDays));
            }
        }
    }

    /// <summary>
    /// Chi linh hoạt thực tế (đặc tả §7): Debit không phải khoản định kỳ của đáp án, không vay, không chuyển ví, không góp quỹ, đã trừ hoàn tiền
    /// bằng <see cref="RefundNetting.ForHabits"/> trên cửa sổ.
    /// </summary>
    private static long FlexibleActual(ForecastHarness harness, IReadOnlyDictionary<Guid, TxLabel> labels, DateOnly from, DateOnly to)
        => RefundNetting.ForHabits(harness.TruthRaw(from, to).Where(t => !t.IsLoan).ToList())
            .Where(t => t.Type == TransactionType.Debit && labels[t.Id] != TxLabel.Recurring)
            .Sum(t => t.AmountCents);

    private static bool IsAffected(SynthWorld world, DateOnly origin, int horizon)
        => world.Scenario == ScenarioCodes.K6 || world.EventDays.Any(d => d >= origin && d <= origin.AddDays(horizon));

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
