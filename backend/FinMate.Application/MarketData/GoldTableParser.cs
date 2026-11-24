using System.Globalization;
using System.Text.RegularExpressions;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

/// <summary>Giá một lượng vàng, đồng.</summary>
public sealed record GoldReading(string GoldKey, long BuyCents, long SellCents);

/// <summary>
/// Đọc bảng giá vàng trong chữ của trang. Luật nằm ở <see cref="GoldSettings"/>. Câu văn như "vàng miếng SJC lên mốc 135 triệu đồng/lượng" không thành dòng giá,
/// vì sau tên chỉ được có số, số tăng giảm, phần trăm và vài chữ như "mua vào", "bán ra". Đọc được không có nghĩa là đúng: vẫn phải đủ nguồn khớp.
/// </summary>
public static class GoldTableParser
{
    private const int UnitSearchChars = 4000;

    private static readonly Regex Token = new(@"\S+", RegexOptions.CultureInvariant);
    private static readonly Regex Grouped = new(@"\A[+-]?\d{1,3}(?:[.,]\d{3})+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Plain = new(@"\A[+-]?\d+\z", RegexOptions.CultureInvariant);
    private static readonly Regex Percent = new(@"\A\(?[+-]?\d+(?:[.,]\d+)?%\)?\z", RegexOptions.CultureInvariant);
    private static readonly Regex UnitLabel = new(@"Đơn vị\s*:\s*(?<unit>[^\d:]{1,40}?(?:lượng|chỉ))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SlashSpaces = new(@"\s*/\s*", RegexOptions.CultureInvariant);

    public static IReadOnlyList<GoldReading> Parse(string pageText, GoldSettings settings)
    {
        var readings = new List<GoldReading>();
        if (!settings.Enabled)
        {
            return readings;
        }

        foreach (var row in settings.Rows)
        {
            if (FirstValid(pageText, row, settings) is { } reading)
            {
                readings.Add(reading);
            }
        }

        return readings;
    }

    public static bool IsValid(GoldReading reading, GoldSettings settings)
        => reading.BuyCents >= settings.MinPriceCents && reading.SellCents <= settings.MaxPriceCents
            && reading.BuyCents <= reading.SellCents
            && (reading.SellCents - reading.BuyCents) * 100 <= reading.SellCents * settings.MaxSpreadPercent;

    private static GoldReading? FirstValid(string text, GoldRow row, GoldSettings settings)
    {
        var names = string.Join('|', row.Names.OrderByDescending(n => n.Length).Select(n => string.Join(@"\s+", n.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))));
        foreach (Match name in Regex.Matches(text, $@"(?<![\p{{L}}\d])(?:{names})(?![\p{{L}}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var numbers = RowNumbers(text, name.Index + name.Length, settings);
            if (numbers.Count < 2 || UnitAfter(text, name.Index, settings) is not { } multiplier)
            {
                continue;
            }

            var prices = numbers.Select(n => n * multiplier).Where(p => p >= settings.MinPriceCents && p <= settings.MaxPriceCents).Take(2).ToList();
            if (prices.Count == 2 && new GoldReading(row.Key, prices[0], prices[1]) is var reading && IsValid(reading, settings))
            {
                return reading;
            }
        }

        return null;
    }

    /// <summary>Các số ngay sau tên dòng, dừng ở chữ đầu tiên không được phép (tên thương hiệu khác, câu văn).</summary>
    private static List<long> RowNumbers(string text, int start, GoldSettings settings)
    {
        var numbers = new List<long>();
        foreach (Match token in Token.Matches(text, start))
        {
            var value = token.Value;
            if (Grouped.IsMatch(value) || Plain.IsMatch(value))
            {
                if (value[0] is not ('+' or '-') && long.TryParse(value.Replace(".", string.Empty).Replace(",", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    numbers.Add(number);
                }
            }
            else if (!Percent.IsMatch(value) && !settings.RowWords.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return numbers;
    }

    /// <summary>Hệ số của nhãn "Đơn vị: …" gần nhất phía sau dòng. Không có nhãn hoặc nhãn lạ thì không đọc dòng đó.</summary>
    private static long? UnitAfter(string text, int start, GoldSettings settings)
    {
        var match = UnitLabel.Match(text, start, Math.Min(UnitSearchChars, text.Length - start));
        if (!match.Success)
        {
            return null;
        }

        var unit = SlashSpaces.Replace(match.Groups["unit"].Value.Trim(), "/");
        return settings.Units.FirstOrDefault(u => string.Equals(SlashSpaces.Replace(u.Text.Trim(), "/"), unit, StringComparison.OrdinalIgnoreCase))?.Multiplier;
    }
}
