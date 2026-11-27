namespace FinMate.Application.Kpi;

/// <summary>Phép gộp của dashboard KPI. Tỉ lệ luôn tính lại từ tổng của kỳ, không lấy trung bình các tỉ lệ ngày (D11).</summary>
public static class KpiMath
{
    /// <summary>D16: thông báo thành giao dịch trên số thông báo đáng xét (bỏ ví không theo dõi và bản gửi lại). Mẫu số 0 thì null.</summary>
    public static double? ConversionRate(long converted, long received, long ignored, long duplicate)
    {
        var eligible = received - ignored - duplicate;
        return eligible <= 0 ? null : Math.Round((double)converted / eligible, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Che từng ô của một nhóm con; giữ đủ các khoá được yêu cầu, khoá không có số là 0.</summary>
    public static IReadOnlyDictionary<string, KpiCount> MaskCells(IReadOnlyDictionary<string, long> cells, IEnumerable<string> keys, int minCellSize)
        => keys.Concat(cells.Keys).Distinct(StringComparer.Ordinal)
            .ToDictionary(k => k, k => KpiCount.Mask(cells.TryGetValue(k, out var v) ? v : 0, minCellSize), StringComparer.Ordinal);
}
