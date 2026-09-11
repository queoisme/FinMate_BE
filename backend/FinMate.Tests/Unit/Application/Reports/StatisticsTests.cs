using FinMate.Application.Reports;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

public class StatisticsTests
{
    [Fact]
    public void Median_OddCount_IsMiddleValue()
        => Statistics.Median(new double[] { 5, 1, 3 }).Should().Be(3);

    [Fact]
    public void Median_EvenCount_AveragesTheTwoMiddleValues()
        => Statistics.Median(new double[] { 1, 2, 3, 4 }).Should().Be(2.5);

    [Fact]
    public void Median_Empty_IsZero()
        => Statistics.Median(Array.Empty<double>()).Should().Be(0);

    [Fact]
    public void MedianAbsoluteDeviation_IdenticalValues_IsZero()
        => Statistics.MedianAbsoluteDeviation(new double[] { 7, 7, 7, 7 }).Should().Be(0);

    [Fact]
    public void MedianAbsoluteDeviation_IgnoresASingleExtremeValue()
    {
        // Độ lệch chuẩn sẽ bị 1000 thổi phồng; MAD thì không.
        var mad = Statistics.MedianAbsoluteDeviation(new double[] { 10, 10, 10, 10, 10, 1000 });

        mad.Should().Be(0);
    }

    [Fact]
    public void UpperOutlierBound_AllValuesEqual_FallsBackToTwiceTheMedian()
    {
        // MAD = 0, nếu không có fallback thì mọi giá trị nhỉnh hơn trung vị 1 đồng đều bị
        // coi là outlier.
        Statistics.UpperOutlierBound(new double[] { 100, 100, 100 }).Should().Be(200);
    }

    [Fact]
    public void TrimUpperOutliers_DropsTheExtremeValueAndKeepsTheRest()
    {
        var trimmed = Statistics.TrimUpperOutliers(new double[] { 100, 110, 90, 105, 50_000 });

        trimmed.Should().NotContain(50_000);
        trimmed.Should().HaveCount(4);
    }

    [Fact]
    public void TrimUpperOutliers_TooFewValues_KeepsThemAll()
    {
        // Với 2 điểm thì không có cách nào phân biệt outlier với xu hướng.
        var values = new double[] { 100, 50_000 };

        Statistics.TrimUpperOutliers(values).Should().BeEquivalentTo(values);
    }

    [Fact]
    public void TrimUpperOutliers_NeverReturnsEmpty()
    {
        // Chuỗi toàn 0: bound = 0, mọi giá trị <= bound nên vẫn còn dữ liệu để tính trung vị.
        Statistics.TrimUpperOutliers(new double[] { 0, 0, 0 }).Should().NotBeEmpty();
    }
}
