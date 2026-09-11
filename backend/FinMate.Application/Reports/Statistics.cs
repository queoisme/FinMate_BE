namespace FinMate.Application.Reports;

/// <summary>
/// Thống kê chống outlier dùng chung cho forecast và insight "chi tiêu bất thường".
/// Dùng trung vị + MAD thay vì trung bình + độ lệch chuẩn: một giao dịch mua sắm lớn kéo
/// lệch trung bình và đồng thời thổi phồng độ lệch chuẩn, nên nó vừa làm dự báo sai vừa tự
/// che giấu chính mình khỏi bị phát hiện là outlier.
/// </summary>
internal static class Statistics
{
    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Median absolute deviation — độ phân tán quanh trung vị.</summary>
    internal static double MedianAbsoluteDeviation(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var median = Median(values);
        return Median(values.Select(v => Math.Abs(v - median)).ToArray());
    }

    /// <summary>
    /// Ngưỡng trên để coi là bất thường: <c>median + thresholdInMads · MAD</c>.
    /// Khi MAD = 0 (mọi giá trị giống nhau) thì lùi về bội số của trung vị, nếu không mọi
    /// giá trị nhỉnh hơn trung vị một đồng cũng thành outlier.
    /// </summary>
    internal static double UpperOutlierBound(IReadOnlyList<double> values, double thresholdInMads = 3.0)
    {
        var median = Median(values);
        var mad = MedianAbsoluteDeviation(values);

        return mad > 0
            ? median + (thresholdInMads * mad)
            : median * 2;
    }

    /// <summary>Bỏ các giá trị vượt ngưỡng outlier; luôn giữ lại ít nhất 1 giá trị.</summary>
    internal static IReadOnlyList<double> TrimUpperOutliers(IReadOnlyList<double> values, double thresholdInMads = 3.0)
    {
        if (values.Count < 3)
        {
            return values;
        }

        var bound = UpperOutlierBound(values, thresholdInMads);
        var trimmed = values.Where(v => v <= bound).ToArray();

        return trimmed.Length > 0 ? trimmed : values;
    }
}
