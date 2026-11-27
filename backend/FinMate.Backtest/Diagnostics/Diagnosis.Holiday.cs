using System.Text;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;

namespace FinMate.Backtest.Diagnostics;

public static partial class Diagnosis
{
    private const int HolidayHorizon = 14;
    private static readonly DateOnly CheckOrigin = new(2026, 2, 5);

    /// <summary>
    /// C5: K5h so với K5 theo hồ sơ (mốc bị ảnh hưởng ở tầm 14), và kiểm hệ số ngày lễ áp vào đâu: tỉ lệ chi linh hoạt dự kiến, thận trọng và lạc quan
    /// từng ngày giữa K5h và K5 trên cùng người và mốc.
    /// </summary>
    private static void WriteHoliday(StringBuilder md, RunOptions options, Action<string>? progress)
    {
        md.Append($"### C5. K5h (khai Tết 150%) so với K5 (không khai), tầm {HolidayHorizon}, mốc bị ảnh hưởng (ô = thiên lệch / MAE, triệu đồng)\n\n");
        md.Append("| Hồ sơ | n | Sai số số dư K5 | Sai số số dư K5h | Chi linh hoạt K5 | Chi linh hoạt K5h | Thu không định kỳ K5 | Thu định kỳ K5 |\n|---|---|---|---|---|---|---|---|\n");
        var check = new MultiplierCheck();
        foreach (var persona in options.Personas)
        {
            var (k5, k5h) = (new Accumulator(), new Accumulator());
            var n = 0;
            for (var seed = 0; seed < options.SeedsPerPersona; seed++)
            {
                var world = WorldBuilder.Build(persona, seed, ScenarioCodes.K5, options.RootSeed);
                n += CollectHoliday(world, options.Settings, k5);
                CollectHoliday(world with { Scenario = ScenarioCodes.K5h }, WithTet(options.Settings), k5h);
                CheckMultiplier(world, options, check);
            }

            md.Append($"| {persona} | {n} | {Cell(k5.TotalError[(persona, HolidayHorizon)])} | {Cell(k5h.TotalError[(persona, HolidayHorizon)])} | "
                      + $"{Cell(k5.Decomposition[(persona, HolidayHorizon, Component.Flexible)])} | {Cell(k5h.Decomposition[(persona, HolidayHorizon, Component.Flexible)])} | "
                      + $"{Cell(k5.Decomposition[(persona, HolidayHorizon, Component.IncomeIrregular)])} | {Cell(k5.Decomposition[(persona, HolidayHorizon, Component.IncomeRecurring)])} |\n");
            progress?.Invoke($"chẩn đoán K5/K5h: {persona}");
        }

        md.Append($"\nKiểm hệ số (mốc {Iso(CheckOrigin)}, tầm 30, mọi người gốc): {check.Describe()}\n\n");
    }

    private static int CollectHoliday(SynthWorld world, Application.Admin.SystemConfig.ForecastSettings settings, Accumulator acc)
    {
        var harness = new ForecastHarness(world, settings);
        var truth = world.Transactions.ToDictionary(t => t.Id);
        var count = 0;
        foreach (var origin in BacktestRunner.Origins(world, 1).Where(o => world.EventDays.Any(d => d >= o && d <= o.AddDays(HolidayHorizon))))
        {
            var forecast = harness.ForecastAt(origin, HolidayHorizon);
            var to = origin.AddDays(HolidayHorizon);
            var actual = Components.Actual(harness.TruthRaw(origin, to), harness.TruthBoundary(origin, to), truth);
            var balance = harness.ActualBalance(forecast.StartBalanceCents, origin, to);
            acc.AddDecomposition(world.Persona, HolidayHorizon, Components.Predicted(forecast, HolidayHorizon), actual, forecast.Days[HolidayHorizon].ExpectedBalanceCents - balance);
            count++;
        }

        return count;
    }

    private static void CheckMultiplier(SynthWorld world, RunOptions options, MultiplierCheck check)
    {
        var plain = new ForecastHarness(world, options.Settings).ForecastAt(CheckOrigin, 30);
        var tet = new ForecastHarness(world, WithTet(options.Settings)).ForecastAt(CheckOrigin, 30);
        var plainLowSpend = SpendPerDay(plain, d => d.LowBalanceCents);
        var tetLowSpend = SpendPerDay(tet, d => d.LowBalanceCents);
        for (var i = 0; i < plain.Days.Count; i++)
        {
            var date = plain.Days[i].Date;
            var inside = date >= GeneratorSpec.HolidayConfigStart && date <= GeneratorSpec.HolidayConfigEnd;
            check.Add(inside, plain.Days[i].FlexibleSpendCents, tet.Days[i].FlexibleSpendCents, plainLowSpend[i], tetLowSpend[i]);
        }
    }

    /// <summary>Chi linh hoạt của một đường (thận trọng) mỗi ngày, suy từ chênh lệch số dư giữa hai ngày liền nhau trừ sự kiện "tất cả các đường".</summary>
    private static long[] SpendPerDay(CashflowForecastDto f, Func<CashflowDayDto, long> line)
    {
        var spend = new long[f.Days.Count];
        var previous = f.StartBalanceCents;
        for (var i = 0; i < f.Days.Count; i++)
        {
            var events = f.Days[i].Events.Where(e => !e.OptimisticOnly).Sum(e => e.AmountCents);
            spend[i] = previous + events - line(f.Days[i]);
            previous = line(f.Days[i]);
        }

        return spend;
    }

    private sealed class MultiplierCheck
    {
        private long _insideDays;
        private long _insideExact;
        private long _outsideDays;
        private long _outsideExact;

        public void Add(bool inside, long plain, long tet, long plainLow, long tetLow)
        {
            var exact = inside
                ? tet == Scaled(plain) && tetLow == Scaled(plainLow)
                : tet == plain && tetLow == plainLow;
            if (inside)
            {
                _insideDays++;
                _insideExact += exact ? 1 : 0;
            }
            else
            {
                _outsideDays++;
                _outsideExact += exact ? 1 : 0;
            }
        }

        public string Describe()
            => $"trong khoảng {GeneratorSpec.HolidayConfigStart:yyyy-MM-dd} → {GeneratorSpec.HolidayConfigEnd:yyyy-MM-dd}, chi dự kiến và chi thận trọng của K5h = 150% của K5 (làm tròn như engine) ở "
               + $"{_insideExact}/{_insideDays} ngày; ngoài khoảng, bằng đúng K5 ở {_outsideExact}/{_outsideDays} ngày.";

        private static long Scaled(long amount) => (long)Math.Round(amount * GeneratorSpec.HolidayConfigMultiplierPercent / 100.0, MidpointRounding.AwayFromZero);
    }
}
