using System.Globalization;

namespace FinMate.Application.Forecasting;

/// <summary>Định dạng số tiền và ngày trong nội dung cảnh báo, cùng kiểu với thông báo giao dịch (<c>1.234.000đ</c>, <c>dd/MM</c>).</summary>
internal static class ForecastText
{
    private static readonly NumberFormatInfo VietnamMoney = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Money(long amount) => amount.ToString("N0", VietnamMoney) + "đ";

    /// <summary>Phần trăm → "bao nhiêu lần", một chữ số thập phân, dấu phẩy kiểu Việt: 350 → "3,5".</summary>
    public static string Times(int percent) => (percent / 100.0).ToString("0.#", VietnamMoney);

    public static string Day(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
