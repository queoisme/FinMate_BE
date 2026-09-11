using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class DailySummaryJobTests
{
    private static readonly DateOnly Date = new(2026, 9, 10);

    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly DailySummaryJob _job;

    public DailySummaryJobTests()
    {
        _job = new DailySummaryJob(_reportRepository.Object);
    }

    private void SetupUser(Guid userId, DailySpendPoint? point, params CategorySpend[] categories)
    {
        _reportRepository.Setup(r => r.GetUserIdsWithActivityAsync(Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { userId });
        _reportRepository.Setup(r => r.GetDailySpendAsync(userId, Date, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(point is null ? new List<DailySpendPoint>() : new List<DailySpendPoint> { point });
        _reportRepository.Setup(r => r.GetCategorySpendAsync(userId, Date, Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories.ToList());
    }

    [Fact]
    public async Task Summarise_WritesTotalsAndTopCategory()
    {
        var userId = Guid.NewGuid();
        var topCategory = Guid.NewGuid();
        SetupUser(
            userId,
            new DailySpendPoint(Date, 450_000, 2_000_000, 4),
            new CategorySpend(topCategory, "Ăn uống", "food", 300_000),
            new CategorySpend(Guid.NewGuid(), "Di chuyển", "transport", 150_000));

        DailySummary? written = null;
        _reportRepository.Setup(r => r.UpsertDailySummaryAsync(It.IsAny<DailySummary>(), It.IsAny<CancellationToken>()))
            .Callback<DailySummary, CancellationToken>((s, _) => written = s)
            .Returns(Task.CompletedTask);

        await _job.SummariseAsync(Date);

        written.Should().NotBeNull();
        written!.SummaryDate.Should().Be(Date);
        written.TotalSpentCents.Should().Be(450_000);
        written.TotalIncomeCents.Should().Be(2_000_000);
        written.TransactionCount.Should().Be(4);
        written.TopCategoryId.Should().Be(topCategory);
        written.TopCategorySpentCents.Should().Be(300_000);
    }

    [Fact]
    public async Task Summarise_IncomeOnlyDay_LeavesTopCategoryUnset()
    {
        var userId = Guid.NewGuid();
        SetupUser(userId, new DailySpendPoint(Date, 0, 5_000_000, 1));

        DailySummary? written = null;
        _reportRepository.Setup(r => r.UpsertDailySummaryAsync(It.IsAny<DailySummary>(), It.IsAny<CancellationToken>()))
            .Callback<DailySummary, CancellationToken>((s, _) => written = s)
            .Returns(Task.CompletedTask);

        await _job.SummariseAsync(Date);

        written!.TopCategoryId.Should().BeNull();
        written.TopCategorySpentCents.Should().Be(0);
    }

    [Fact]
    public async Task Summarise_UncategorizedSpendOnly_DoesNotPickItAsTopCategory()
    {
        var userId = Guid.NewGuid();
        SetupUser(
            userId,
            new DailySpendPoint(Date, 100_000, 0, 1),
            new CategorySpend(null, null, null, 100_000));

        DailySummary? written = null;
        _reportRepository.Setup(r => r.UpsertDailySummaryAsync(It.IsAny<DailySummary>(), It.IsAny<CancellationToken>()))
            .Callback<DailySummary, CancellationToken>((s, _) => written = s)
            .Returns(Task.CompletedTask);

        await _job.SummariseAsync(Date);

        written!.TotalSpentCents.Should().Be(100_000);
        written.TopCategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Summarise_NoUsersWithActivity_WritesNothing()
    {
        _reportRepository.Setup(r => r.GetUserIdsWithActivityAsync(Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        await _job.SummariseAsync(Date);

        _reportRepository.Verify(
            r => r.UpsertDailySummaryAsync(It.IsAny<DailySummary>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
