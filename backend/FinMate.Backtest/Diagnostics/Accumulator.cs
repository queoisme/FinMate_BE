using FinMate.Application.Forecasting;

namespace FinMate.Backtest.Diagnostics;

public static class FlexMethod
{
    public const string Production = "production";
    public const string TrimmedMedian = "(a) trung vị có cắt";
    public const string Median = "(b) trung vị không cắt";
    public const string Mean = "(c) trung bình không cắt";

    public static readonly IReadOnlyList<string> All = [Production, TrimmedMedian, Median, Mean];
}

/// <summary>Cộng dồn số đo chẩn đoán theo (hồ sơ, tầm). Thuần bộ nhớ, tất định.</summary>
public sealed class Accumulator
{
    public sealed class Sum
    {
        public long N;
        public double Signed;
        public double Absolute;
        public double Actual;

        public void Add(double error, double actual = 0)
        {
            N++;
            Signed += error;
            Absolute += Math.Abs(error);
            Actual += actual;
        }
    }

    public sealed class BandSum
    {
        public long N;
        public long BalanceInside;
        public long EngineFlexInside;
        public double EngineFlexWidth;
        public long SumQuantileN;
        public long SumQuantileInside;
        public double SumQuantileWidth;
    }

    public Dictionary<(string Persona, int Horizon, Component Component), Sum> Decomposition { get; } = new();

    public Dictionary<(string Persona, int Horizon), Sum> TotalError { get; } = new();

    public Dictionary<(string Persona, int Horizon, string Method), Sum> Flex { get; } = new();

    public Dictionary<(string Persona, int Horizon), BandSum> Band { get; } = new();

    /// <summary>Phase 97 Task 1: (hồ sơ, tầm, "flex"|"income", phương án) → sai số so với thực tế.</summary>
    public Dictionary<(string Persona, int Horizon, string Kind, string Method), Sum> Estimates { get; } = new();

    /// <summary>Phase 97 Task 8: (hồ sơ, tầm, phương án dải) → số mốc, số mốc số dư thật nằm trong dải, tổng độ rộng.</summary>
    public Dictionary<(string Persona, int Horizon, string Method), Coverage> SumBand { get; } = new();

    public sealed class Coverage
    {
        public long N;
        public long Inside;
        public double Width;
        public long Skipped;
    }

    public long IdentityMismatches { get; private set; }

    public long ReplicationMismatches { get; private set; }

    public long ReplicationChecked { get; private set; }

    public long FlexSkipped { get; private set; }

    public void AddDecomposition(string persona, int horizon, ComponentFlows predicted, ComponentFlows actual, long balanceError)
    {
        long sum = 0;
        foreach (var c in Components.All)
        {
            var error = predicted[c] - actual[c];
            sum += error;
            Get(Decomposition, (persona, horizon, c)).Add(error);
        }

        Get(TotalError, (persona, horizon)).Add(balanceError);
        if (sum != balanceError)
        {
            IdentityMismatches++;
        }
    }

    public void AddFlex(string persona, int horizon, FlexVariants.Levels? levels, DateOnly origin, long actual, long production)
    {
        Get(Flex, (persona, horizon, FlexMethod.Production)).Add(production - actual, actual);
        if (levels is null)
        {
            FlexSkipped++;
            return;
        }

        var trimmed = FlexVariants.Over(levels.TrimmedMedian, origin, horizon);
        ReplicationChecked++;
        if (FlexVariants.Over(levels.Mean, origin, horizon) != production)   // Phase 97: production = trung bình theo thứ (F1), trước đó = (a)
        {
            ReplicationMismatches++;
        }

        Get(Flex, (persona, horizon, FlexMethod.TrimmedMedian)).Add(trimmed - actual, actual);
        Get(Flex, (persona, horizon, FlexMethod.Median)).Add(FlexVariants.Over(levels.Median, origin, horizon) - actual, actual);
        Get(Flex, (persona, horizon, FlexMethod.Mean)).Add(FlexVariants.Over(levels.Mean, origin, horizon) - actual, actual);
    }

    /// <summary>
    /// Dải: (1) số dư thật trong [thận trọng, lạc quan]; (2) chi linh hoạt thật trong dải chi của engine (Σ P25 … Σ P75 theo ngày, suy từ ba đường số dư);
    /// (3) chi linh hoạt thật trong P25–P75 của tổng h+1 ngày lấy từ lịch sử.
    /// </summary>
    public void AddBand(string persona, int horizon, CashflowForecastDto forecast, FlexVariants.Levels? levels, long flexActual, long actualBalance)
    {
        var b = Get(Band, (persona, horizon));
        var day = forecast.Days[horizon];
        var expectedFlex = forecast.Days.Take(horizon + 1).Sum(d => d.FlexibleSpendCents);
        var optimistic = forecast.Days.Take(horizon + 1).SelectMany(d => d.Events).Where(e => e.OptimisticOnly).Sum(e => e.AmountCents);
        var highSpend = expectedFlex + (day.ExpectedBalanceCents - day.LowBalanceCents);
        var lowSpend = expectedFlex + (day.ExpectedBalanceCents - day.HighBalanceCents) + optimistic;
        b.N++;
        b.BalanceInside += actualBalance >= Math.Min(day.LowBalanceCents, day.HighBalanceCents) && actualBalance <= Math.Max(day.LowBalanceCents, day.HighBalanceCents) ? 1 : 0;
        b.EngineFlexInside += flexActual >= lowSpend && flexActual <= highSpend ? 1 : 0;
        b.EngineFlexWidth += highSpend - lowSpend;
        if (levels is not null && FlexVariants.SumQuantiles(levels.Series, horizon) is { } q)
        {
            b.SumQuantileN++;
            b.SumQuantileInside += flexActual >= q.Low && flexActual <= q.High ? 1 : 0;
            b.SumQuantileWidth += q.High - q.Low;
        }
    }

    public void AddSumBand(string persona, int horizon, string method, (long Low, long High)? band, long actualBalance)
    {
        var c = Get(SumBand, (persona, horizon, method));
        if (band is not { } b)
        {
            c.Skipped++;
            return;
        }

        c.N++;
        c.Inside += actualBalance >= b.Low && actualBalance <= b.High ? 1 : 0;
        c.Width += b.High - b.Low;
    }

    public void AddEstimate(string persona, int horizon, string kind, string method, long predicted, long actual)
        => Get(Estimates, (persona, horizon, kind, method)).Add(predicted - actual, actual);

    private static TValue Get<TKey, TValue>(Dictionary<TKey, TValue> map, TKey key)
        where TKey : notnull
        where TValue : new()
    {
        if (!map.TryGetValue(key, out var value))
        {
            map[key] = value = new TValue();
        }

        return value;
    }
}
