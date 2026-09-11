using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Common;

public class VietnamTimeTests
{
    [Theory]
    // 17:00Z là 00:00 hôm sau theo giờ VN — mốc chuyển ngày.
    [InlineData("2026-09-30T16:59:59+00:00", 2026, 9, 30)]
    [InlineData("2026-09-30T17:00:00+00:00", 2026, 10, 1)]
    [InlineData("2026-10-01T10:00:00+00:00", 2026, 10, 1)]
    public void DateOf_RollsOverAtVietnamMidnight(string instant, int year, int month, int day)
    {
        VietnamTime.DateOf(DateTimeOffset.Parse(instant))
            .Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void DayRange_CoversExactlyOneVietnamDay_AtUtcOffset()
    {
        var (start, end) = VietnamTime.DayRange(new DateOnly(2026, 10, 1));

        // 01/10 giờ VN bắt đầu lúc 30/09 17:00Z và kết thúc lúc 01/10 17:00Z.
        start.Should().Be(new DateTimeOffset(2026, 9, 30, 17, 0, 0, TimeSpan.Zero));
        end.Should().Be(new DateTimeOffset(2026, 10, 1, 17, 0, 0, TimeSpan.Zero));

        // Npgsql chỉ ghi được offset 0 vào cột timestamptz.
        start.Offset.Should().Be(TimeSpan.Zero);
        end.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void DayRange_Spanning_CoversBothEndpointDays()
    {
        var (start, end) = VietnamTime.DayRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        start.Should().Be(VietnamTime.DayRange(new DateOnly(2026, 10, 1)).Start);
        end.Should().Be(VietnamTime.DayRange(new DateOnly(2026, 10, 3)).End);
        (end - start).TotalDays.Should().Be(3);
    }

    [Fact]
    public void MonthRange_EndIsNextVietnamMonthStart_NotUtcPlusOneMonth()
    {
        // Tháng 10 có 31 ngày: cộng 1 tháng lên biểu diễn UTC của mốc đầu tháng sẽ lệch 1 ngày.
        var (start, end) = VietnamTime.MonthRange(2026, 10);

        end.Should().Be(VietnamTime.MonthRange(2026, 11).Start);
        start.AddMonths(1).Should().NotBe(end);
    }

    [Fact]
    public void YearMonthOf_ReadsVietnamCalendar_NotTheUtcRepresentation()
    {
        var (start, _) = VietnamTime.MonthRange(2026, 10);

        // start là 2026-09-30T17:00Z — đọc thẳng .Month sẽ ra 9.
        start.Month.Should().Be(9);
        VietnamTime.YearMonthOf(start).Should().Be((2026, 10));
    }
}
