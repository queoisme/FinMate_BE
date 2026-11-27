using System.Globalization;
using System.Text;

namespace FinMate.Backtest.Reporting;

/// <summary>Định dạng số cho báo cáo. Markdown dùng kiểu Việt Nam (1.234.567 đ, 12,3%); CSV dùng số trơn, dấu chấm thập phân (máy đọc).</summary>
public static class Format
{
    /// <summary>S3: tỉ lệ / CI với ít hơn chừng này mẫu thì ghi "chưa đủ mẫu".</summary>
    public const int MinSamples = 20;

    public const string NotEnough = "chưa đủ mẫu";

    public static string Money(long cents)
    {
        var digits = Math.Abs(cents).ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                builder.Append('.');
            }

            builder.Append(digits[i]);
        }

        return (cents < 0 ? "−" : string.Empty) + builder + " đ";
    }

    public static string Pct(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + "%" : "—";

    /// <summary>Tỉ lệ kèm CI theo S3; dưới <see cref="MinSamples"/> mẫu thì "chưa đủ mẫu".</summary>
    public static string PctCi(double? value, double? low, double? high, int n)
        => n < MinSamples ? NotEnough : value is null ? "—" : $"{Pct(value)} (CI 95%: {Pct(low)} – {Pct(high)})";

    public static string Csv(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Csv(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Csv(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

    public static string Csv(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
