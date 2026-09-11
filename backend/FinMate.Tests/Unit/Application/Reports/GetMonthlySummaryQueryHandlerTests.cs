using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Reports.Queries;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

public class GetMonthlySummaryQueryHandlerTests
{
    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly GetMonthlySummaryQueryHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public GetMonthlySummaryQueryHandlerTests()
    {
        _handler = new GetMonthlySummaryQueryHandler(_reportRepository.Object);

        // Mặc định: không có gì, từng test override khoảng mình quan tâm.
        _reportRepository.Setup(r => r.GetDailySummariesAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailySummary>());
        _reportRepository.Setup(r => r.GetDailySpendAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailySpendPoint>());
    }

    private static DailySummary Summary(DateOnly date, long spent, long income = 0, int count = 1) => new()
    {
        Id = Guid.NewGuid(),
        SummaryDate = date,
        TotalSpentCents = spent,
        TotalIncomeCents = income,
        TransactionCount = count,
    };

    private void SetupSummaries(DateOnly from, DateOnly to, params DailySummary[] summaries)
        => _reportRepository.Setup(r => r.GetDailySummariesAsync(_userId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries.ToList());

    private void SetupLive(DateOnly from, DateOnly to, params DailySpendPoint[] points)
        => _reportRepository.Setup(r => r.GetDailySpendAsync(_userId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(points.ToList());

    [Fact]
    public async Task Handle_PastMonth_ReadsOnlyPrecomputedSummaries()
    {
        // Tháng đã khép lại hoàn toàn → không được đụng vào aggregate live.
        var first = new DateOnly(2026, 1, 1);
        var last = new DateOnly(2026, 1, 31);
        SetupSummaries(first, last, Summary(first, 300_000, 1_000_000, 2), Summary(last, 200_000, 0, 1));

        var result = await _handler.HandleAsync(new GetMonthlySummaryQuery(_userId, 2026, 1));

        result.TotalSpentCents.Should().Be(500_000);
        result.TotalIncomeCents.Should().Be(1_000_000);
        result.NetCents.Should().Be(500_000);
        result.TransactionCount.Should().Be(3);

        _reportRepository.Verify(r => r.GetDailySpendAsync(
            _userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CurrentMonth_AddsTodaysLiveSpendOnTopOfSummaries()
    {
        var today = VietnamTime.Today();
        var (year, month) = (today.Year, today.Month);
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var yesterday = today.AddDays(-1);

        if (today.Day > 1)
        {
            SetupSummaries(first, yesterday, Summary(first, 400_000, 0, 2));
        }

        // DailySummaryJob mới chạy tới hết hôm qua — hôm nay phải lấy live, nếu không
        // "tháng này" luôn thiếu đúng ngày hiện tại.
        SetupLive(today, last, new DailySpendPoint(today, 150_000, 50_000, 1));

        var result = await _handler.HandleAsync(new GetMonthlySummaryQuery(_userId, year, month));

        var expectedSpent = today.Day > 1 ? 550_000 : 150_000;
        result.TotalSpentCents.Should().Be(expectedSpent);
        result.TotalIncomeCents.Should().Be(50_000);
    }

    [Fact]
    public async Task Handle_ComparesAgainstPreviousMonthSpend()
    {
        var first = new DateOnly(2026, 3, 1);
        var last = new DateOnly(2026, 3, 31);
        var prevFirst = new DateOnly(2026, 2, 1);
        var prevLast = new DateOnly(2026, 2, 28);

        SetupSummaries(first, last, Summary(first, 1_200_000));
        SetupSummaries(prevFirst, prevLast, Summary(prevFirst, 1_000_000));

        var result = await _handler.HandleAsync(new GetMonthlySummaryQuery(_userId, 2026, 3));

        result.PrevMonthSpentCents.Should().Be(1_000_000);
        result.ChangePercent.Should().Be(20.0);
    }

    [Fact]
    public async Task Handle_NoPreviousMonthSpend_LeavesChangeUndefinedRatherThanClaimingInfinity()
    {
        SetupSummaries(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Summary(new DateOnly(2026, 3, 1), 500_000));

        var result = await _handler.HandleAsync(new GetMonthlySummaryQuery(_userId, 2026, 3));

        result.PrevMonthSpentCents.Should().Be(0);
        result.ChangePercent.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsVietnamLocalPeriodBounds()
    {
        var result = await _handler.HandleAsync(new GetMonthlySummaryQuery(_userId, 2026, 9));

        result.PeriodStart.Offset.Should().Be(TimeSpan.FromHours(7));
        result.PeriodStart.Day.Should().Be(1);
        result.PeriodEnd.Should().Be(result.PeriodStart.AddMonths(1));
    }
}
