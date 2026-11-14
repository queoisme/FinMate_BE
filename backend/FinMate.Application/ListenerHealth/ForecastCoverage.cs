using FinMate.Application.Forecasting;

namespace FinMate.Application.ListenerHealth;

/// <summary>
/// BR-LH-08: gắn độ phủ vào response dự báo mà KHÔNG đổi con số hay độ rộng dải. <c>poor</c> chỉ hạ độ tin cậy xuống <c>low</c> và thêm một câu cảnh báo;
/// <c>unknown</c> (app chưa báo) không đổi gì. Engine và <c>ForecastAccuracyService</c> không đi qua đây (D10), nên thống kê độ chính xác vẫn đo độ tin cậy của engine.
/// </summary>
public static class ForecastCoverage
{
    public const string LowConfidence = "low";

    public static DataCoverageSummaryDto Summarize(CoverageResult coverage)
    {
        var level = ListenerHealthText.Level(coverage.Level);
        return new DataCoverageSummaryDto(coverage.Percent, level, coverage.WindowDays, coverage.Level == CoverageLevel.Poor ? Warning(coverage.Percent ?? 0) : null);
    }

    /// <summary>Câu mẫu, không qua AI. Con số duy nhất trong câu chính là <c>percent</c>, để bộ kiểm số của Agent đối chiếu được (S5).</summary>
    public static string Warning(int percent)
        => $"Chỉ {percent}% số ngày gần đây FinMate đọc được thông báo ngân hàng, nên dự báo có thể thiếu giao dịch.";

    public static CashflowForecastDto Apply(CashflowForecastDto forecast, DataCoverageSummaryDto coverage)
        => coverage.IsPoor ? forecast with { Confidence = LowConfidence, DataCoverage = coverage } : forecast with { DataCoverage = coverage };

    public static SafeToSpendDto Apply(SafeToSpendDto spendable, DataCoverageSummaryDto coverage) => spendable with { DataCoverage = coverage };
}
