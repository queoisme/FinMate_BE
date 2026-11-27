using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Evaluation;

public static class ScoreWindows
{
    public const string All = "all";

    /// <summary>Mốc mà đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (đặc tả §6); với K6 là mọi mốc được chấm.</summary>
    public const string Affected = "affected";
}

public sealed record GroupKey(string Mode, string Scenario, string Persona, string Confidence, int Horizon, string Window);

/// <param name="ImprovementPct">Với baseline: mô hình tốt hơn baseline này bao nhiêu % (1 − MAE mô hình / MAE baseline); âm là kém hơn. Null với chính mô hình.</param>
public sealed record BalanceMetric(
    GroupKey Key, string Method, int N, int Users, long Mae, long MedianAe, long Bias, double? ScaleRelPct, double? ImprovementPct, double? CiLow, double? CiHigh);

public sealed record BandMetric(GroupKey Key, int N, int Users, double CoveragePct, double CiLow, double CiHigh, long MeanWidth, long MedianWidth);

/// <param name="WapePct">Σ|dự báo − thực tế| / Σ thực tế.</param>
/// <param name="BiasPct">Σ(dự báo − thực tế) / Σ thực tế; âm là dự báo chi ít hơn thực tế.</param>
/// <param name="MapePct">Chỉ trên mốc có thực tế ≥ <see cref="MetricsBook.MapeFloorCents"/>; <paramref name="MapeExcluded"/> mốc bị loại.</param>
public sealed record FlexMetric(
    GroupKey Key, string Method, int N, int Users, double? WapePct, double? CiLow, double? CiHigh, double? BiasPct, double? MapePct, int MapeN, int MapeExcluded);

/// <param name="ChangePct">MAE của mô hình ở kịch bản so với MAE ở <paramref name="Control"/> trên cùng (người, mốc, tầm): dương là kém đi.</param>
public sealed record ScenarioEffect(
    string Mode, string Scenario, string Control, int Horizon, int N, int Users, long MaeScenario, long MaeControl, double? ChangePct, double? CiLow, double? CiHigh);

public sealed record MetricsResult(
    IReadOnlyList<BalanceMetric> Balance, IReadOnlyList<BandMetric> Band, IReadOnlyList<FlexMetric> Flexible, IReadOnlyList<ScenarioEffect> Effects);

/// <summary>Tính mọi bảng chỉ số tầng 1 từ các <see cref="OriginRow"/> (D10). Hàm thuần, tất định (bootstrap có hạt cố định theo nhóm).</summary>
public static class MetricsBook
{
    public const long MapeFloorCents = 100_000;
    public const long ScaleFloorCents = 100_000;
    public const string AllPersonas = "all";
    public const string AllConfidence = "all";

    public static MetricsResult Compute(IReadOnlyList<OriginRow> rows)
    {
        var groups = Group(rows);
        var balance = new List<BalanceMetric>();
        var band = new List<BandMetric>();
        var flexible = new List<FlexMetric>();
        foreach (var (key, members) in groups.OrderBy(g => g.Key, KeyOrder.Instance))
        {
            balance.AddRange(Methods.Balance.Select(m => BalanceOf(key, members, m)));
            band.Add(BandOf(key, members));
            flexible.AddRange(Methods.Flexible.Select(m => FlexOf(key, members, m)));
        }

        return new MetricsResult(balance, band, flexible, Effects(rows));
    }

    private static Dictionary<GroupKey, List<OriginRow>> Group(IReadOnlyList<OriginRow> rows)
    {
        var groups = new Dictionary<GroupKey, List<OriginRow>>();
        foreach (var r in rows)
        {
            var confidence = r.LowConfidence ? "low" : "normal";
            foreach (var persona in new[] { r.Persona, AllPersonas })
            {
                foreach (var conf in new[] { confidence, AllConfidence })
                {
                    Add(groups, new GroupKey(r.Mode, r.Scenario, persona, conf, r.Horizon, ScoreWindows.All), r);
                    if (r.Affected)
                    {
                        Add(groups, new GroupKey(r.Mode, r.Scenario, persona, conf, r.Horizon, ScoreWindows.Affected), r);
                    }
                }
            }
        }

        return groups;
    }

    private static void Add(Dictionary<GroupKey, List<OriginRow>> groups, GroupKey key, OriginRow row)
    {
        if (!groups.TryGetValue(key, out var list))
        {
            groups[key] = list = [];
        }

        list.Add(row);
    }

    private static BalanceMetric BalanceOf(GroupKey key, List<OriginRow> rows, string method)
    {
        var errors = rows.Select(r => r.Predicted(method) - r.Actual).ToList();
        var abs = errors.Select(Math.Abs).ToList();
        var mae = Mean(abs);
        var scale = rows.Average(r => (double)Math.Abs(r.Actual - r.Start));
        double? improvement = null;
        (double Low, double High)? ci = null;
        if (method != Methods.Model)
        {
            var perUser = PerUser(rows, r => Math.Abs(r.Expected - r.Actual), r => Math.Abs(r.Predicted(method) - r.Actual));
            improvement = Ratio(perUser) is { } ratio ? Pct(1 - ratio) : null;
            ci = Bootstrap.Interval(perUser, Seed(key, method)) is { } interval ? (Pct(1 - interval.High), Pct(1 - interval.Low)) : null;
        }

        return new BalanceMetric(
            key, method, rows.Count, Users(rows), mae, Median(abs), Mean(errors), scale >= ScaleFloorCents ? Pct(mae / scale) : null,
            improvement, ci?.Low, ci?.High);
    }

    private static BandMetric BandOf(GroupKey key, List<OriginRow> rows)
    {
        var perUser = PerUser(rows, r => r.InsideBand ? 1 : 0, _ => 1);
        var interval = Bootstrap.Interval(perUser, Seed(key, "band"));
        var widths = rows.Select(r => r.BandWidth).ToList();
        return new BandMetric(
            key, rows.Count, Users(rows), Pct((double)rows.Count(r => r.InsideBand) / rows.Count), Pct(interval?.Low ?? 0), Pct(interval?.High ?? 0),
            Mean(widths), Median(widths));
    }

    private static FlexMetric FlexOf(GroupKey key, List<OriginRow> rows, string method)
    {
        var perUser = PerUser(rows, r => Math.Abs(r.PredictedFlexible(method) - r.FlexActual), r => r.FlexActual);
        var actual = rows.Sum(r => (double)r.FlexActual);
        var interval = Bootstrap.Interval(perUser, Seed(key, $"flex|{method}"));
        var mapeRows = rows.Where(r => r.FlexActual >= MapeFloorCents).ToList();
        double? mape = mapeRows.Count == 0 ? null : Pct(mapeRows.Average(r => Math.Abs((double)(r.PredictedFlexible(method) - r.FlexActual)) / r.FlexActual));
        return new FlexMetric(
            key, method, rows.Count, Users(rows),
            actual > 0 ? Pct(rows.Sum(r => Math.Abs((double)(r.PredictedFlexible(method) - r.FlexActual))) / actual) : null,
            interval is null ? null : Pct(interval.Value.Low), interval is null ? null : Pct(interval.Value.High),
            actual > 0 ? Pct(rows.Sum(r => (double)(r.PredictedFlexible(method) - r.FlexActual)) / actual) : null,
            mape, mapeRows.Count, rows.Count - mapeRows.Count);
    }

    /// <summary>Kịch bản so với đối chứng trên cùng (người, mốc, tầm), chỉ ở các mốc trong cửa sổ ảnh hưởng. K5h so với K5, còn lại so với K0.</summary>
    private static IReadOnlyList<ScenarioEffect> Effects(IReadOnlyList<OriginRow> rows)
    {
        var index = rows.ToDictionary(r => (r.Mode, r.Scenario, r.User, r.Origin, r.Horizon));
        var effects = new List<ScenarioEffect>();
        foreach (var g in rows.Where(r => r.Affected).GroupBy(r => (r.Mode, r.Scenario, r.Horizon)).OrderBy(g => g.Key.Mode).ThenBy(g => g.Key.Scenario).ThenBy(g => g.Key.Horizon))
        {
            foreach (var control in g.Key.Scenario == ScenarioCodes.K5h ? new[] { ScenarioCodes.K0, ScenarioCodes.K5 } : [ScenarioCodes.K0])
            {
                var pairs = g.Select(r => (Row: r, Control: index.GetValueOrDefault((r.Mode, control, r.User, r.Origin, r.Horizon))))
                    .Where(p => p.Control is not null)
                    .ToList();
                if (pairs.Count == 0)
                {
                    continue;
                }

                var perUser = pairs.GroupBy(p => p.Row.User)
                    .Select(u => (Num: u.Sum(p => (double)Math.Abs(p.Row.Expected - p.Row.Actual)), Den: u.Sum(p => (double)Math.Abs(p.Control!.Expected - p.Control.Actual))))
                    .ToList();
                var interval = Bootstrap.Interval(perUser, $"effect|{g.Key.Mode}|{g.Key.Scenario}|{control}|{g.Key.Horizon}");
                effects.Add(new ScenarioEffect(
                    g.Key.Mode, g.Key.Scenario, control, g.Key.Horizon, pairs.Count, perUser.Count,
                    Mean(pairs.Select(p => Math.Abs(p.Row.Expected - p.Row.Actual)).ToList()),
                    Mean(pairs.Select(p => Math.Abs(p.Control!.Expected - p.Control.Actual)).ToList()),
                    Ratio(perUser) is { } ratio ? Pct(ratio - 1) : null,
                    interval is null ? null : Pct(interval.Value.Low - 1), interval is null ? null : Pct(interval.Value.High - 1)));
            }
        }

        return effects;
    }

    private static List<(double Num, double Den)> PerUser(IEnumerable<OriginRow> rows, Func<OriginRow, long> num, Func<OriginRow, long> den)
        => rows.GroupBy(r => r.User).OrderBy(g => g.Key).Select(g => (g.Sum(r => (double)num(r)), g.Sum(r => (double)den(r)))).ToList();

    private static double? Ratio(IReadOnlyList<(double Num, double Den)> perUser)
    {
        var den = perUser.Sum(u => u.Den);
        return den > 0 ? perUser.Sum(u => u.Num) / den : null;
    }

    private static string Seed(GroupKey key, string what) => $"bootstrap|{key.Mode}|{key.Scenario}|{key.Persona}|{key.Confidence}|{key.Horizon}|{key.Window}|{what}";

    private static int Users(IEnumerable<OriginRow> rows) => rows.Select(r => r.User).Distinct().Count();

    private static long Mean(IReadOnlyList<long> values)
        => values.Count == 0 ? 0 : (long)Math.Round(values.Average(v => (double)v), MidpointRounding.AwayFromZero);

    private static long Median(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Pct(double fraction) => Math.Round(fraction * 100, 1, MidpointRounding.AwayFromZero);

    private sealed class KeyOrder : IComparer<GroupKey>
    {
        public static readonly KeyOrder Instance = new();

        public int Compare(GroupKey? x, GroupKey? y)
            => string.CompareOrdinal(Text(x!), Text(y!));

        private static string Text(GroupKey k)
            => $"{k.Mode}|{k.Scenario}|{(k.Persona == AllPersonas ? "0" : k.Persona)}|{(k.Confidence == AllConfidence ? "0" : k.Confidence)}|{k.Horizon:D3}|{k.Window}";
    }
}

/// <summary>Bootstrap theo người (D10): lấy mẫu lại người gốc có hoàn lại, 1.000 lần, thống kê = ΣNum / ΣDen. Hạt cố định theo nhóm.</summary>
public static class Bootstrap
{
    public const int Resamples = 1_000;
    private const double Alpha = 0.05;

    public static (double Low, double High)? Interval(IReadOnlyList<(double Num, double Den)> perUser, string seedKey)
    {
        if (perUser.Count == 0)
        {
            return null;
        }

        var rng = Rng.ForStream(seedKey);
        var stats = new List<double>(Resamples);
        for (var b = 0; b < Resamples; b++)
        {
            double num = 0, den = 0;
            for (var i = 0; i < perUser.Count; i++)
            {
                var pick = perUser[rng.Int(0, perUser.Count - 1)];
                num += pick.Num;
                den += pick.Den;
            }

            if (den > 0)
            {
                stats.Add(num / den);
            }
        }

        if (stats.Count == 0)
        {
            return null;
        }

        stats.Sort();
        var low = stats[(int)Math.Floor(Alpha / 2 * stats.Count)];
        var high = stats[Math.Min(stats.Count - 1, (int)Math.Ceiling((1 - (Alpha / 2)) * stats.Count) - 1)];
        return (low, high);
    }
}
