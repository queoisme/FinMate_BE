using System.Globalization;
using System.Text;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;

namespace FinMate.Backtest.Diagnostics;

public static partial class Diagnosis
{
    private const string AllRegular = "stable_worker + family";
    private const int TraceHorizon = 14;
    private static readonly DateOnly TraceOrigin = new(2025, 10, 15);

    private static readonly Dictionary<Component, string> ComponentNames = new()
    {
        [Component.IncomeRecurring] = "Thu định kỳ",
        [Component.IncomeIrregular] = "Thu không định kỳ",
        [Component.RecurringOutflow] = "Chi định kỳ",
        [Component.Flexible] = "Chi linh hoạt",
        [Component.LoanTransfer] = "Vay / chuyển",
    };

    private static void WriteDecomposition(StringBuilder md, Accumulator acc, IReadOnlyList<string> personas)
    {
        md.Append("### C1. Sai số số dư theo thành phần (dự báo − thực tế; ô = thiên lệch / MAE, triệu đồng)\n\n");
        md.Append($"Kiểm tổng: tổng sai số các thành phần khác sai số số dư ở {acc.IdentityMismatches} dòng (phải là 0).\n\n");
        md.Append("| Hồ sơ | Tầm | Sai số số dư | " + string.Join(" | ", Components.All.Select(c => ComponentNames[c])) + " |\n");
        md.Append("|---|---|---|" + string.Concat(Components.All.Select(_ => "---|")) + "\n");
        foreach (var persona in personas)
        {
            foreach (var h in Horizons)
            {
                var total = acc.TotalError[(persona, h)];
                var cells = Components.All.Select(c => Cell(acc.Decomposition[(persona, h, c)]));
                md.Append($"| {persona} | {h} | {Cell(total)} | {string.Join(" | ", cells)} |\n");
            }
        }

        md.Append('\n');
    }

    private static void WriteFlexVariants(StringBuilder md, Accumulator acc, IReadOnlyList<string> personas)
    {
        md.Append("### C2. Chi linh hoạt theo ba cách tính trên cùng lịch sử (ô = WAPE / thiên lệch so với thực tế)\n\n");
        md.Append($"Dựng lại (c) khớp production (Phase 97: production = trung bình theo thứ; Phase 96 là (a)) ở {acc.ReplicationChecked - acc.ReplicationMismatches}/{acc.ReplicationChecked} mốc×tầm; "
                  + $"{acc.FlexSkipped} mốc×tầm ở nhánh thiếu lịch sử bị bỏ qua.\n\n");
        md.Append("| Hồ sơ | Tầm | " + string.Join(" | ", FlexMethod.All) + " |\n|---|---|" + string.Concat(FlexMethod.All.Select(_ => "---|")) + "\n");
        foreach (var persona in personas.Append(MetricsBook.AllPersonas))
        {
            foreach (var h in Horizons)
            {
                var cells = FlexMethod.All.Select(m => FlexCell(acc, persona, h, m, personas));
                md.Append($"| {persona} | {h} | {string.Join(" | ", cells)} |\n");
            }
        }

        md.Append('\n');
    }

    private static string FlexCell(Accumulator acc, string persona, int h, string method, IReadOnlyList<string> personas)
    {
        var sums = (persona == MetricsBook.AllPersonas ? personas : [persona])
            .Select(p => acc.Flex.GetValueOrDefault((p, h, method)))
            .Where(s => s is not null)
            .ToList();
        var actual = sums.Sum(s => s!.Actual);
        return actual <= 0 ? "—" : $"{Pct(sums.Sum(s => s!.Absolute) / actual)} / {Pct(sums.Sum(s => s!.Signed) / actual)}";
    }

    private static void WriteBands(StringBuilder md, Accumulator acc)
    {
        md.Append("### C4. Dải trên nhóm thu nhập đều (K0)\n\n");
        md.Append("| Nhóm | Tầm | n | Số dư trong dải thận trọng–lạc quan | Chi linh hoạt trong dải chi của engine (Σ phân vị ngày) | Rộng TB | Chi linh hoạt trong P25–P75 của tổng h+1 ngày (lịch sử) | Rộng TB |\n");
        md.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var group in new[] { AllRegular }.Concat(Regular))
        {
            var members = group == AllRegular ? Regular : [group];
            foreach (var h in Horizons)
            {
                var bands = members.Select(p => acc.Band[(p, h)]).ToList();
                var n = bands.Sum(b => b.N);
                var nq = bands.Sum(b => b.SumQuantileN);
                md.Append($"| {group} | {h} | {n} | {Pct((double)bands.Sum(b => b.BalanceInside) / n)} | {Pct((double)bands.Sum(b => b.EngineFlexInside) / n)} | "
                          + $"{Money(bands.Sum(b => b.EngineFlexWidth) / n)} | {(nq == 0 ? "—" : Pct((double)bands.Sum(b => b.SumQuantileInside) / nq))} | "
                          + $"{(nq == 0 ? "—" : Money(bands.Sum(b => b.SumQuantileWidth) / nq))} |\n");
            }
        }

        md.Append('\n');
    }

    /// <summary>C3: một tiểu thương, một mốc, tầm 14: từng ngày, dự báo và thực tế theo thành phần.</summary>
    private static void WriteTrace(StringBuilder md, RunOptions options)
    {
        var world = WorldBuilder.Build(Persona.SmallTrader, 0, ScenarioCodes.K0, options.RootSeed);
        var harness = new ForecastHarness(world, options.Settings, options.ConfigVersion);
        var forecast = harness.ForecastAt(TraceOrigin, TraceHorizon);
        var truth = world.Transactions.ToDictionary(t => t.Id);
        var start = forecast.StartBalanceCents;
        md.Append($"### C3. Truy vết: small_trader #0, mốc {Iso(TraceOrigin)}, tầm {TraceHorizon} (số dư lúc dự báo {Money(start)}, tin cậy {forecast.Confidence}, cơ sở {forecast.Basis})\n\n");
        md.Append("| Ngày | Sự kiện dự báo | Chi linh hoạt dự báo | Số dư dự báo | Thu không định kỳ thật | Chi định kỳ thật | Chi linh hoạt thật | Vay/chuyển thật | Số dư thật | Chênh |\n");
        md.Append("|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var day in forecast.Days)
        {
            var actual = Components.Actual(harness.TruthRaw(day.Date, day.Date), harness.TruthBoundary(day.Date, day.Date), truth);
            var actualBalance = harness.ActualBalance(start, TraceOrigin, day.Date);
            var events = day.Events.Count == 0 ? "—" : string.Join("; ", day.Events.Select(e => $"{e.Label} {Money(e.AmountCents)}"));
            md.Append($"| {Iso(day.Date)} | {events} | {Money(day.FlexibleSpendCents)} | {Money(day.ExpectedBalanceCents)} | {Money(actual[Component.IncomeIrregular])} | "
                      + $"{Money(actual[Component.RecurringOutflow])} | {Money(actual[Component.Flexible])} | {Money(actual[Component.LoanTransfer])} | {Money(actualBalance)} | "
                      + $"{Money(day.ExpectedBalanceCents - actualBalance)} |\n");
        }

        var history = harness.View.Raw(TraceOrigin.AddDays(-90), TraceOrigin);
        md.Append($"\nTrong 90 ngày trước mốc, harness đưa cho `ForecastService` {history.Count(t => t.Type == Domain.Enums.TransactionType.Credit)} khoản thu "
                  + $"(tổng {Money(history.Where(t => t.Type == Domain.Enums.TransactionType.Credit).Sum(t => t.AmountCents))}); khoản định kỳ đang theo dõi (đáp án): "
                  + string.Join(", ", new OracleRecurring(world).ItemsAt(harness.Clock.Now).Select(i => $"{i.Name} ({i.Direction}, {i.Cadence})")) + ".\n\n");
    }

    private static string Cell(Accumulator.Sum s) => s.N == 0 ? "—" : $"{Tr(s.Signed / s.N)} / {Tr(s.Absolute / s.N)}";

    private static string Tr(double cents) => (cents / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',').Replace("-", "−");
}
