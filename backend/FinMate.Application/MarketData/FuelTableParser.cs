using System.Globalization;
using System.Text.RegularExpressions;

namespace FinMate.Application.MarketData;

public sealed record FuelReading(string FuelKey, long Region1PriceCents, long Region2PriceCents);

public static class FuelKeys
{
    public const string E10Ron95Level5 = "e10_ron95_5";
    public const string E10Ron95Level3 = "e10_ron95_3";
    public const string E5Ron92Level2 = "e5_ron92_2";
    public const string Diesel0001Level5 = "do_0001s_5";
    public const string Diesel005Level2 = "do_005s_2";
    public const string Kerosene = "kerosene_2k";

    /// <summary>Thiếu một trong các loại này thì không coi là bảng giá xăng.</summary>
    public static readonly IReadOnlyList<string> Required = [E10Ron95Level5, E10Ron95Level3, E5Ron92Level2];

    public static readonly IReadOnlyList<string> All = [E10Ron95Level5, E10Ron95Level3, E5Ron92Level2, Diesel0001Level5, Diesel005Level2, Kerosene];
}

/// <summary>
/// Đọc bảng giá trong text của trang, cả hai kiểu số <c>22.650</c> và <c>20,890</c>, kể cả khi có cột tăng giảm đứng trước.
/// Sau tên hàng chỉ được phép có số. Nhờ vậy câu văn như "E10 RON 95 mức 3 giảm 2.080 đồng còn 21.150" không bao giờ thành một dòng giá.
/// Đọc được bao nhiêu không có nghĩa là đúng: kết quả vẫn phải qua <see cref="FuelReadingValidator"/>.
/// </summary>
public static class FuelTableParser
{
    private const int MinPlausiblePrice = 1_000;

    private static readonly (string Key, Regex Pattern)[] Rows =
    [
        (FuelKeys.E10Ron95Level5, Row(@"E10\s*RON\s*95(?:\s*-\s*|\s+)(?:m[ứư]c\s*5|V)\b")),
        (FuelKeys.E10Ron95Level3, Row(@"E10\s*RON\s*95(?:\s*-\s*|\s+)(?:m[ứư]c\s*3|III)\b")),
        (FuelKeys.E5Ron92Level2, Row(@"E5\s*RON\s*92(?:\s*-\s*|\s+)(?:m[ứư]c\s*2|II)\b")),
        (FuelKeys.Diesel0001Level5, Row(@"(?:DO|[ĐD]i[êe]zen)\s*0[.,]001\s*S(?:\s*-\s*|\s+)(?:m[ứư]c\s*5|V)\b")),
        (FuelKeys.Diesel005Level2, Row(@"(?:DO|[ĐD]i[êe]zen)\s*0[.,]05\s*S(?:\s*-\s*|\s+)(?:m[ứư]c\s*2|II)\b")),
        (FuelKeys.Kerosene, Row(@"D[ầa]u\s*h[ỏo]a(?:\s*2)?\s*-?\s*K\b")),
    ];

    private static Regex Row(string name) => new(
        name + @"((?:\s+[-+]?\d[\d.,]*)+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static IReadOnlyList<FuelReading> Parse(string text)
    {
        var readings = new List<FuelReading>();
        foreach (var (key, pattern) in Rows)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var prices = Prices(match.Groups[1].Value);
                if (prices.Count >= 2)
                {
                    // Cột giá vùng 1 và vùng 2 luôn đứng cuối hàng.
                    readings.Add(new FuelReading(key, prices[^2], prices[^1]));
                    break;
                }
            }
        }

        return readings;
    }

    private static List<long> Prices(string numbers)
    {
        var prices = new List<long>();
        foreach (var token in numbers.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token[0] is '-' or '+')
            {
                continue;
            }

            var digits = token.Replace(".", string.Empty).Replace(",", string.Empty);
            if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= MinPlausiblePrice)
            {
                prices.Add(value);
            }
        }

        return prices;
    }
}
