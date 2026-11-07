namespace FinMate.Application.Reports;

/// <summary>
/// Thống kê chống outlier dùng chung cho forecast và insight "chi tiêu bất thường".
/// Dùng trung vị + MAD thay vì trung bình + độ lệch chuẩn: một giao dịch mua sắm lớn kéo
/// lệch trung bình và đồng thời thổi phồng độ lệch chuẩn, nên nó vừa làm dự báo sai vừa tự
/// che giấu chính mình khỏi bị phát hiện là outlier.
/// </summary>
public static class Statistics
{
    public static double Median(IReadOnlyList<double> values)
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

    /// <summary>
    /// Phân vị <paramref name="p"/> (0..1) của dãy <b>đã sắp tăng dần</b>, nội suy tuyến tính bằng số nguyên: tiền không đi qua <c>double</c>.
    /// <paramref name="p"/> được làm tròn tới phần nghìn. Dãy rỗng trả 0 (cùng quy ước với <see cref="Median"/>).
    /// </summary>
    public static long Quantile(IReadOnlyList<long> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var permille = (long)Math.Round(Math.Clamp(p, 0.0, 1.0) * 1000);
        var position = permille * (sorted.Count - 1); // vị trí theo phần nghìn của chỉ số
        var lower = (int)(position / 1000);
        var remainder = position % 1000;
        if (remainder == 0)
        {
            return sorted[lower];
        }

        var low = sorted[lower];
        var high = sorted[lower + 1];
        return low + (((high - low) * remainder) + 500) / 1000; // làm tròn nửa lên; dãy sắp tăng nên high >= low
    }

    /// <summary>
    /// Hệ số biến thiên = độ lệch chuẩn (tổng thể) / trung bình. Là tỉ số không có đơn vị dùng để PHÂN LOẠI độ biến động, không phải một khoản tiền, nên dùng <c>double</c>.
    /// Trung bình bằng 0 trả 0.
    /// </summary>
    public static double CoefficientOfVariation(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var mean = values.Average(v => (double)v);
        if (mean == 0)
        {
            return 0;
        }

        var variance = values.Average(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(variance) / Math.Abs(mean);
    }

    /// <summary>Median absolute deviation — độ phân tán quanh trung vị.</summary>
    public static double MedianAbsoluteDeviation(IReadOnlyList<double> values)
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
    public static double UpperOutlierBound(IReadOnlyList<double> values, double thresholdInMads = 3.0)
    {
        var median = Median(values);
        var mad = MedianAbsoluteDeviation(values);

        return mad > 0
            ? median + (thresholdInMads * mad)
            : median * 2;
    }

    /// <summary>Bỏ các giá trị vượt ngưỡng outlier; luôn giữ lại ít nhất 1 giá trị.</summary>
    public static IReadOnlyList<double> TrimUpperOutliers(IReadOnlyList<double> values, double thresholdInMads = 3.0)
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
