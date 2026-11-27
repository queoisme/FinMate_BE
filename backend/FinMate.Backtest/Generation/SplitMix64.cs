using System.Text;

namespace FinMate.Backtest.Generation;

/// <summary>
/// PRNG SplitMix64 (đặc tả bộ sinh §2). Tự viết thay vì <see cref="Random"/> để chuỗi số không phụ thuộc phiên bản .NET.
/// Mỗi luồng ngẫu nhiên có hạt riêng (<see cref="ForStream"/>), nên thêm luồng mới không làm xê dịch luồng cũ.
/// </summary>
public sealed class SplitMix64
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;
    private const ulong FnvOffset = 0xCBF29CE484222325UL;
    private const ulong FnvPrime = 0x100000001B3UL;
    private const double TwoPowMinus53 = 1.0 / (1UL << 53);

    private ulong _state;

    public SplitMix64(ulong seed)
    {
        _state = seed;
    }

    /// <summary>Hạt của một luồng: FNV-1a 64 bit trên khóa UTF-8, rồi trộn một lần bằng SplitMix64.</summary>
    public static SplitMix64 ForStream(string key) => new(Mix(Fnv1a(key)));

    public ulong NextUInt64()
    {
        _state += Golden;
        return Mix(_state);
    }

    /// <summary>Số thực đều trong [0, 1): 53 bit cao.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * TwoPowMinus53;

    internal static ulong Fnv1a(string key)
    {
        var hash = FnvOffset;
        foreach (var b in Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
