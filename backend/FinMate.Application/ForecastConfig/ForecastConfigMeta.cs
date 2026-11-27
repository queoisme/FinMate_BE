using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.ForecastConfig;

/// <summary>
/// Một tham số cho form soạn. <see cref="Min"/>/<see cref="Max"/> là khoảng tuyệt đối của <c>SystemConfigDocumentValidator</c>; ràng buộc giữa hai trường nằm ở
/// <see cref="NotAbove"/> (giá trị ≤ trường kia) và <see cref="Below"/> (giá trị &lt; trường kia). Test phản chiếu giữ bảng này khớp với validator (S6).
/// </summary>
public sealed record ForecastConfigField(
    string Path,
    string Type,
    JsonNode? Default,
    long? Min,
    long? Max,
    string? NotAbove,
    string? Below,
    int? MaxItems,
    string Description);

public static class ForecastConfigMeta
{
    public const string IntegerType = "integer";
    public const string HolidaysType = "holidays";

    private sealed record Rule(long Min, long Max, string Description, string? NotAbove = null, string? Below = null);

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.Ordinal)
    {
        ["forecast.historyWindowDays"] = new(30, 365, "Cửa sổ lịch sử (ngày) dùng để học thói quen chi tiêu."),
        ["forecast.minHistoryDays"] = new(1, 365, "Số ngày có giao dịch tối thiểu; ít hơn thì dự báo mang cờ tin cậy thấp.", NotAbove: "forecast.historyWindowDays"),
        ["forecast.reservePercentOfIncome"] = new(0, 50, "Dự phòng mặc định theo % thu nhập dự kiến tháng."),
        ["forecast.maxHorizonDays"] = new(7, 365, "Dự báo dòng tiền xa nhất (ngày)."),
        ["forecast.amountTolerancePercent"] = new(0, 50, "Số tiền các lần của một khoản định kỳ được lệch nhau tối đa (%)."),
        ["forecast.monthlyMinIntervalDays"] = new(20, 40, "Khoảng cách tối thiểu (ngày) để coi là chu kỳ tháng.", NotAbove: "forecast.monthlyMaxIntervalDays"),
        ["forecast.monthlyMaxIntervalDays"] = new(20, 40, "Khoảng cách tối đa (ngày) để coi là chu kỳ tháng."),
        ["forecast.weeklyMinIntervalDays"] = new(5, 9, "Khoảng cách tối thiểu (ngày) để coi là chu kỳ tuần.", NotAbove: "forecast.weeklyMaxIntervalDays"),
        ["forecast.weeklyMaxIntervalDays"] = new(5, 9, "Khoảng cách tối đa (ngày) để coi là chu kỳ tuần."),
        ["forecast.suggestMinOccurrences"] = new(2, 12, "Số lần lặp để đề xuất khoản định kỳ.", NotAbove: "forecast.autoActivateOccurrences"),
        ["forecast.autoActivateOccurrences"] = new(2, 12, "Số lần lặp để tự ghi nhận khoản định kỳ."),
        ["forecast.overdueGraceDays"] = new(0, 31, "Khoản chi định kỳ quá hạn vẫn được coi là sắp trả trong chừng này ngày."),
        ["forecast.maxScenarioItems"] = new(1, 50, "Số khoản giả định tối đa trong một lần thử kịch bản."),
        ["forecast.purchaseDefaultHorizonDays"] = new(1, 90, "Đánh giá khoản mua tới chừng này ngày khi chưa biết kỳ thu nhập kế tiếp."),
        ["forecast.purchaseStaleBalanceDays"] = new(1, 60, "Số dư ví cũ hơn chừng này ngày thì đánh giá khoản mua chỉ ở mức một phần."),
        ["forecast.lowBalanceHorizonDays"] = new(3, 60, "Cảnh báo số dư thấp nhìn trước chừng này ngày."),
        ["forecast.lowBalanceCooldownDays"] = new(1, 30, "Đã cảnh báo số dư thấp thì chừng này ngày sau mới gửi lại."),
        ["forecast.inaccurateSuppressDays"] = new(1, 60, "Người dùng báo cảnh báo không chính xác thì tắt loại đó chừng này ngày."),
        ["forecast.budgetRiskMinElapsedDays"] = new(1, 28, "Vượt ngân sách sớm: đã qua chừng này ngày trong tháng thì dùng nhịp chi tháng này."),
        ["forecast.budgetRiskMinDaysRemaining"] = new(1, 28, "Vượt ngân sách sớm chỉ báo khi còn ít nhất chừng này ngày."),
        ["forecast.billReminderDaysBefore"] = new(0, 14, "Nhắc hoá đơn trước hạn chừng này ngày."),
        ["forecast.incomeLateDays"] = new(1, 15, "Quá ngày dự kiến chừng này ngày mà chưa thấy thu nhập thì báo."),
        ["forecast.irregularIncomeMonths"] = new(2, 12, "Thu nhập không đều ước tính từ chừng này tháng gần nhất."),
        ["forecast.quietHoursStartMinute"] = new(0, 1439, "Giờ yên lặng mặc định bắt đầu (phút trong ngày, giờ VN)."),
        ["forecast.quietHoursEndMinute"] = new(0, 1439, "Giờ yên lặng mặc định kết thúc (phút trong ngày, giờ VN)."),
        ["forecast.anomalyLookbackDays"] = new(30, 365, "Bất thường: so với các khoản chi trong chừng này ngày."),
        ["forecast.anomalyMinCategorySamples"] = new(3, 50, "Cần ít nhất chừng này khoản cùng danh mục để so."),
        ["forecast.anomalyMinMerchantSamples"] = new(2, 50, "So theo người nhận cần ít nhất chừng này lần trả trước."),
        ["forecast.anomalyRatioPercent"] = new(150, 5000, "Gấp ít nhất chừng này % mức thông thường mới là bất thường.", NotAbove: "forecast.anomalyHighRatioPercent"),
        ["forecast.anomalyHighRatioPercent"] = new(150, 5000, "Gấp ít nhất chừng này % thì là mức cao (có thông báo)."),
        ["forecast.anomalyMinAmountCents"] = new(0, 100_000_000, "Khoản dưới số tiền này (đồng) không bao giờ bị coi là bất thường."),
        ["forecast.duplicateWindowMinutes"] = new(1, 120, "Hai khoản giống nhau trong chừng này phút thì nghi trùng."),
        ["forecast.normalFeedbackTolerancePercent"] = new(100, 300, "Đã xác nhận bình thường thì khoản tới chừng này % không bị báo nữa."),
        ["forecast.anomalyScanDays"] = new(1, 14, "Quét bất thường các khoản chi trong chừng này ngày gần nhất."),
        ["forecast.categoryForecastMonths"] = new(1, 12, "Dự báo theo danh mục dùng chừng này tháng trọn vẹn gần nhất."),
        ["forecast.categoryForecastMinMonths"] = new(1, 12, "Cần dữ liệu ít nhất chừng này tháng; ít hơn thì độ tin cậy thấp.", NotAbove: "forecast.categoryForecastMonths"),
        ["forecast.categoryChangePercent"] = new(1, 100, "Danh mục thay đổi nhiều khi lệch ít nhất chừng này % so với tháng trước."),
        ["forecast.budgetSuggestionRoundingCents"] = new(1, 1_000_000, "Hạn mức đề xuất làm tròn lên bội số này (đồng)."),
        ["forecast.goalRateMonths"] = new(1, 24, "Tốc độ góp mục tiêu tính từ tối đa chừng này tháng."),
        ["forecast.goodCoveragePercent"] = new(0, 100, "Độ phủ dữ liệu từ mức này trở lên là tốt."),
        ["forecast.minCoveragePercent"] = new(0, 100, "Độ phủ dưới mức này là kém (độ tin cậy thấp).", NotAbove: "forecast.goodCoveragePercent"),
        ["limits.alertT1Percent"] = new(1, 98, "Ngưỡng nhắc nhẹ (T1) mặc định của ngân sách, khi phương pháp và người dùng chưa đặt.", Below: "limits.alertT2Percent"),
        ["limits.alertT2Percent"] = new(2, 99, "Ngưỡng cảnh báo mạnh (T2) mặc định; vượt hạn mức luôn là 100%."),
    };

    private const string HolidaysPath = "forecast.holidays";
    public const int MaxHolidays = 30;

    /// <summary>Mọi tham số của hai nhóm, theo thứ tự khai báo, kèm giá trị mặc định của code.</summary>
    public static IReadOnlyList<ForecastConfigField> Fields()
    {
        var defaults = ForecastConfigSections.Of(new SystemConfigDocument()).ToNode();
        return Paths().Select(path => Field(path, Get(defaults, path))).ToList();
    }

    /// <summary>Đường dẫn camelCase của mọi thuộc tính trong <see cref="ForecastSettings"/> và <see cref="LimitSettings"/>.</summary>
    public static IEnumerable<string> Paths()
        => Props<ForecastSettings>(ForecastConfigSections.ForecastKey).Concat(Props<LimitSettings>(ForecastConfigSections.LimitsKey));

    private static IEnumerable<string> Props<T>(string section)
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{section}.{JsonNamingPolicy.CamelCase.ConvertName(p.Name)}");

    private static ForecastConfigField Field(string path, JsonNode? value)
    {
        if (path == HolidaysPath)
        {
            return new(path, HolidaysType, value?.DeepClone(), null, null, null, null, MaxHolidays,
                "Khoảng ngày lễ: tên, ngày bắt đầu, ngày kết thúc (tối đa 32 ngày), hệ số chi tiêu linh hoạt 50–300%.");
        }

        var rule = Rules.TryGetValue(path, out var found) ? found : throw new InvalidOperationException($"Thiếu mô tả cho tham số {path}.");
        return new(path, IntegerType, value?.DeepClone(), rule.Min, rule.Max, rule.NotAbove, rule.Below, null, rule.Description);
    }

    private static JsonNode? Get(JsonObject root, string path)
    {
        var parts = path.Split('.');
        return root[parts[0]]![parts[1]];
    }
}
