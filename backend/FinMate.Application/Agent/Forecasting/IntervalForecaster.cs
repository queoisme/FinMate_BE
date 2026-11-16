using FinMate.Application.Reports;

namespace FinMate.Application.Agent;

/// <summary>Nhóm thu nhập. <see cref="Unknown"/> = chưa đủ lịch sử để kết luận (dưới <see cref="AgentLimits.MinHistoryMonths"/> tháng).</summary>
public enum IncomeSegment
{
    Unknown,
    Stable,
    Variable,
}

/// <summary>Dải thu nhập hằng tháng (đồng VND): Q10/Q50/Q90 của các tháng ĐÃ TRỌN trong lịch sử, cùng hệ số biến thiên. Lương cố định thì ba mốc trùng nhau.</summary>
public sealed record IncomeProfile(IncomeSegment Segment, long Q10, long Q50, long Q90, int MonthsOfHistory, double Cv);

/// <summary>Dựng <see cref="IncomeProfile"/> từ chuỗi thu nhập theo tháng. Thuần hàm: hai nhóm người dùng chỉ khác nhau ở độ rộng của dải, không có hai bộ máy.</summary>
public static class IntervalForecaster
{
    public static IncomeProfile BuildProfile(IReadOnlyList<long> monthlyIncomeVnd, IncomeSegment? segmentOverride = null)
    {
        var sorted = monthlyIncomeVnd.OrderBy(v => v).ToArray();
        var cv = Statistics.CoefficientOfVariation(sorted);

        var computed = sorted.Length < AgentLimits.MinHistoryMonths
            ? IncomeSegment.Unknown
            : cv >= AgentLimits.VariableSegmentCv ? IncomeSegment.Variable : IncomeSegment.Stable;

        return new IncomeProfile(
            segmentOverride ?? computed,
            Statistics.Quantile(sorted, 0.1),
            Statistics.Quantile(sorted, 0.5),
            Statistics.Quantile(sorted, 0.9),
            sorted.Length,
            cv);
    }
}
