using System.Runtime.CompilerServices;
using FinMate.Application.Economics;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Economics;

/// <summary>Bộ đọc thông cáo CPI của Cục Thống kê. Dữ liệu mẫu là trang thật tháng 10/2026 (đã tải ngày 2026-11-09).</summary>
public class CpiReleaseParserTests
{
    private static string Fixture([CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "Fixtures", "cpi-release-2026-10.html"));

    [Fact]
    public void ParsesTheOctober2026Release()
    {
        var release = CpiReleaseParser.TryParse(Fixture());

        release.Should().NotBeNull();
        release!.Year.Should().Be(2026);
        release.Month.Should().Be(10);
        release.AveragePercentBps.Should().Be(457);   // bình quân mười tháng tăng 4,57% so với cùng kỳ
        release.YearOnYearBps.Should().Be(509);       // tháng 10 so với cùng kỳ năm trước: 5,09%
        release.MonthOnMonthBps.Should().Be(20);      // so với tháng trước: 0,20%
        release.PublishedOn.Should().Be(new DateOnly(2026, 11, 3));
    }

    // Trang thật các tháng trước (tải 2026-11-09, xem docs/economic-feeds-probe.md): cùng một cấu trúc câu dù nằm ở hai chuyên mục khác nhau của trang.
    [Theory]
    [InlineData("09", 9, 452, 508, 62, 2026, 10, 3)]
    [InlineData("08", 8, 445, 489, 47, 2026, 9, 3)]
    public void ParsesEarlierRealReleases(string file, int month, int average, int yoy, int mom, int y, int m, int d)
    {
        var path = Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "..", "Fixtures", $"cpi-release-2026-{file}.html");

        var release = CpiReleaseParser.TryParse(File.ReadAllText(path));

        release.Should().NotBeNull();
        (release!.Month, release.AveragePercentBps, release.YearOnYearBps, release.MonthOnMonthBps, release.PublishedOn)
            .Should().Be((month, average, yoy, mom, new DateOnly(y, m, d)));
    }

    private static string Here([CallerFilePath] string here = "") => here;

    [Fact]
    public void ReturnsNullOnAnUnrelatedPage()
        => CpiReleaseParser.TryParse("<html><body><h1>Thị trường lao động 9 tháng năm 2026</h1><p>Lao động tăng 1,2%.</p></body></html>").Should().BeNull();

    [Fact]
    public void ReturnsNullWhenTheAveragePhraseIsMissing()
    {
        var html = Fixture().Replace("Bình quân mười tháng năm 2026, CPI tăng 4,57% so với cùng kỳ năm trước", "Số liệu bình quân sẽ công bố sau");

        CpiReleaseParser.TryParse(html).Should().BeNull();
    }

    [Theory]
    [InlineData("4,57", 457)]
    [InlineData("4.57", 457)]
    [InlineData("4,5", 450)]
    [InlineData("4", 400)]
    public void AcceptsCommaOrDotDecimals(string written, int expectedBps)
    {
        var html = Fixture().Replace("CPI tăng 4,57% so với cùng kỳ", $"CPI tăng {written}% so với cùng kỳ");

        CpiReleaseParser.TryParse(html)!.AveragePercentBps.Should().Be(expectedBps);
    }

    [Fact]
    public void ADeclineIsNegative()
    {
        var html = Fixture().Replace("CPI tăng 4,57% so với cùng kỳ", "CPI giảm 0,35% so với cùng kỳ");

        CpiReleaseParser.TryParse(html)!.AveragePercentBps.Should().Be(-35);
    }

    [Fact]
    public void RejectsAnImplausibleValue()
    {
        var html = Fixture().Replace("CPI tăng 4,57% so với cùng kỳ", "CPI tăng 45,7% so với cùng kỳ");

        CpiReleaseParser.TryParse(html).Should().BeNull();
    }
}
