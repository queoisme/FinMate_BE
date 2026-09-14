using FinMate.Application.Common.Interfaces;
using FinMate.Application.Reports;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

public class SpendingForecasterTests
{
    // 2026-09-11 12:00Z = 19:00 giờ VN ngày 11/09 → đã qua 11 ngày của tháng 30 ngày.
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private const int DaysElapsed = 11;
    private const int DaysInMonth = 30;
    private static readonly DateOnly MonthFirst = new(2026, 9, 1);

    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly StatisticalSpendingForecaster _forecaster;

    public SpendingForecasterTests()
    {
        _forecaster = new StatisticalSpendingForecaster(_reportRepository.Object, () => Now);
    }

    private void SetupDays(params DailySpendPoint[] points)
        => _reportRepository.Setup(r => r.GetDailySpendAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(points.ToList());

    private static DailySpendPoint Day(DateOnly date, long spent) => new(date, spent, 0, 1);

    [Fact]
    public async Task Forecast_NoHistory_ReturnsZeroWithLowConfidence()
    {
        SetupDays();

        var result = await _forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        result.SpentSoFarCents.Should().Be(0);
        result.ProjectedSpendCents.Should().Be(0);
        result.Confidence.Should().Be("low");
        result.DaysElapsed.Should().Be(DaysElapsed);
        result.DaysInMonth.Should().Be(DaysInMonth);
    }

    [Fact]
    public async Task Forecast_SteadyCurrentMonth_KeepsActualSpendAndExtrapolatesRemainingDays()
    {
        SetupDays(Enumerable.Range(0, DaysElapsed)
            .Select(i => Day(MonthFirst.AddDays(i), 100_000))
            .ToArray());

        var result = await _forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        result.SpentSoFarCents.Should().Be(100_000L * DaysElapsed);
        result.ProjectedSpendCents.Should().Be(100_000L * DaysInMonth);
    }

    [Fact]
    public async Task Forecast_SingleHugeDay_IsNotDraggedUpByThatOutlier()
    {
        // Một ngày mua sắm 50tr giữa các ngày 100k — trung vị + MAD phải bỏ qua nó.
        SetupDays(Enumerable.Range(0, DaysElapsed)
            .Select(i => Day(MonthFirst.AddDays(i), i == 0 ? 50_000_000 : 100_000))
            .ToArray());

        var result = await _forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        var spentSoFar = 50_000_000L + (100_000L * (DaysElapsed - 1));
        result.SpentSoFarCents.Should().Be(spentSoFar);
        // Phần đã tiêu giữ nguyên số thật; phần còn lại chỉ ngoại suy theo 100k/ngày.
        result.ProjectedSpendCents.Should().Be(spentSoFar + (100_000L * (DaysInMonth - DaysElapsed)));
    }

    [Fact]
    public async Task Forecast_DaysWithNoSpendCountAsZero_NotSkipped()
    {
        // Chỉ 2/11 ngày có chi tiêu: run-rate phải là 0, không phải 100k/ngày.
        SetupDays(Day(MonthFirst, 100_000), Day(MonthFirst.AddDays(1), 100_000));

        var result = await _forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        result.SpentSoFarCents.Should().Be(200_000);
        result.ProjectedSpendCents.Should().Be(200_000);
    }

    [Theory]
    [InlineData(0, 0, "low")]        // chưa ghi gì: 11 ngày trống KHÔNG phải 11 ngày bằng chứng
    [InlineData(9, 20, "medium")]    // 11 ngày tháng này + 9 ngày lịch sử
    [InlineData(49, 60, "high")]     // 11 + 49
    public async Task Forecast_ConfidenceTracksHowMuchDataItSaw(
        int historyDays, int expectedBasedOnDays, string expected)
    {
        SetupDays(Enumerable.Range(1, historyDays)
            .Select(i => Day(MonthFirst.AddDays(-i), 50_000))
            .ToArray());

        var result = await _forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        result.BasedOnDays.Should().Be(expectedBasedOnDays);
        result.Confidence.Should().Be(expected);
    }

    [Fact]
    public async Task Forecast_EarlyInTheMonth_LeansOnPreviousMonthsInsteadOfAThinRunRate()
    {
        // Ngày 3 của tháng: mới có 3 điểm dữ liệu, quá mỏng để ngoại suy cả tháng từ chúng,
        // nên phải dùng trung vị 200k/ngày của các tháng trước.
        var earlyNow = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var forecaster = new StatisticalSpendingForecaster(_reportRepository.Object, () => earlyNow);

        var history = Enumerable.Range(1, 40).Select(i => Day(MonthFirst.AddDays(-i), 200_000));
        SetupDays(history.Append(Day(MonthFirst, 10_000)).ToArray());

        var result = await forecaster.ForecastCurrentMonthAsync(Guid.NewGuid());

        result.DaysElapsed.Should().Be(3);
        result.SpentSoFarCents.Should().Be(10_000);
        result.ProjectedSpendCents.Should().Be(10_000 + (200_000L * (DaysInMonth - 3)));
    }
}
