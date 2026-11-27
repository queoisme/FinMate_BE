namespace FinMate.Backtest.Generation;

/// <summary>
/// Các phân phối của đặc tả bộ sinh §2 trên một luồng <see cref="SplitMix64"/>. Số tiền làm tròn tới 1.000đ ngay sau phép biến đổi (S4),
/// để sai khác dấu phẩy động ở chữ số cuối không đổi được kết quả.
/// </summary>
public sealed class Rng
{
    public const long MoneyStep = 1_000;

    private readonly SplitMix64 _source;

    public Rng(SplitMix64 source)
    {
        _source = source;
    }

    public static Rng ForStream(string key) => new(SplitMix64.ForStream(key));

    public double NextDouble() => _source.NextDouble();

    /// <summary>Số thực đều trong [a, b).</summary>
    public double Uniform(double a, double b) => a + ((b - a) * NextDouble());

    /// <summary>Số nguyên đều trong [min, max] (gồm hai đầu).</summary>
    public int Int(int min, int max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        var span = (ulong)(max - min) + 1;
        return min + (int)(_source.NextUInt64() % span);
    }

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    /// <summary>Số tiền đều trong [a, b], làm tròn tới <paramref name="step"/> (đặc tả §5: mặc định 100.000đ).</summary>
    public long Money(long a, long b, long step = 100_000) => RoundTo(Uniform(a, b), step);

    /// <summary>Chuẩn tắc Box–Muller; mỗi lần gọi dùng hai số đều và chỉ lấy một giá trị.</summary>
    public double Normal()
    {
        var u1 = 1.0 - NextDouble();   // (0, 1]: tránh log(0)
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Log-normal với trung vị <paramref name="median"/>, làm tròn tới 1.000đ, nhỏ nhất 1.000đ.</summary>
    public long LogNormalMoney(double median, double sigma)
        => Math.Max(MoneyStep, RoundTo(median * Math.Exp(sigma * Normal()), MoneyStep));

    /// <summary>Poisson theo Knuth (đặc tả: λ ≤ 10).</summary>
    public int Poisson(double lambda)
    {
        if (lambda <= 0)
        {
            return 0;
        }

        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= NextDouble();
        }
        while (p > limit);

        return k - 1;
    }

    public static long RoundTo(double amount, long step)
        => (long)Math.Round(amount / step, MidpointRounding.AwayFromZero) * step;
}
