using FinMate.Application.Reports;

namespace FinMate.Application.Forecasting;

/// <summary>Mức theo thứ trong tuần (chỉ số mảng là <see cref="DayOfWeek"/>): đường dự kiến và độ lệch của hai đầu dải so với nó.</summary>
/// <param name="Expected">Trung bình của mỗi thứ (Phase 97, F1).</param>
/// <param name="Above">Đầu trên của dải = dự kiến + chừng này (≥ 0).</param>
/// <param name="Below">Đầu dưới của dải = dự kiến − chừng này (≥ 0, và không cho đầu dưới âm).</param>
public sealed record DailyLevelSet(long[] Expected, long[] Above, long[] Below);

/// <summary>
/// Ước lượng mức theo ngày cho dòng tiền linh hoạt (chi ở Phase 81+, thu từ Phase 97). Hàm thuần.
/// <list type="bullet">
/// <item><b>Đường dự kiến = trung bình của từng thứ</b> (Phase 97, Task 1, phương án F1): đại lượng cần dự báo là một TỔNG, mà kỳ vọng của tổng là tổng các
/// trung bình. Trung vị (có cắt ngoại lai) dự báo thấp hơn thực tế 24–43% với chi tiêu đuôi nặng (backtest Phase 96); với thu dồn vào vài ngày,
/// trung vị gần như luôn bằng 0.</item>
/// <item><b>Dải giữ độ rộng cũ</b> (S1, tạm đến khi có dải của tổng): trên = dự kiến + (P75 − trung vị), dưới = dự kiến − (trung vị − P25), các phân vị tính
/// trên dữ liệu đã cắt ngoại lai trên như trước Phase 97. Dời tâm theo đường dự kiến để dải không dồn về một phía khi trung bình vượt P75.</item>
/// </list>
/// Thứ nào dưới <see cref="MinSamplesPerWeekday"/> mẫu thì dùng mọi ngày. Chuỗi phải có mọi ngày, ngày không có giao dịch = 0.
/// </summary>
public static class DailyLevels
{
    public const int MinSamplesPerWeekday = 3;
    private const int DaysPerWeek = 7;
    private const double LowerQuantile = 0.25;
    private const double UpperQuantile = 0.75;

    public static DailyLevelSet From(IReadOnlyList<(DateOnly Date, double Amount)> series)
    {
        var all = series.Select(s => s.Amount).ToArray();
        var expected = new long[DaysPerWeek];
        var above = new long[DaysPerWeek];
        var below = new long[DaysPerWeek];
        for (var dow = 0; dow < DaysPerWeek; dow++)
        {
            var samples = series.Where(s => (int)s.Date.DayOfWeek == dow).Select(s => s.Amount).ToArray();
            var values = samples.Length >= MinSamplesPerWeekday ? samples : all;
            var trimmed = Statistics.TrimUpperOutliers(values);
            var median = Statistics.Median(trimmed);
            expected[dow] = Round(values.Average());
            above[dow] = Math.Max(0, Round(Statistics.Quantile(trimmed, UpperQuantile) - median));
            below[dow] = Math.Min(expected[dow], Math.Max(0, Round(median - Statistics.Quantile(trimmed, LowerQuantile))));
        }

        return new DailyLevelSet(expected, above, below);
    }

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
