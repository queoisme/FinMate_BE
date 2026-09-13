using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

public class BudgetPeriodServiceTests
{
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly Mock<IBudgetAlertNotifier> _alertNotifier = new();
    private readonly BudgetPeriodService _service;

    public BudgetPeriodServiceTests()
    {
        _alertNotifier
            .Setup(n => n.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _service = new BudgetPeriodService(_budgetRepository.Object, _alertNotifier.Object);
    }

    private static Budget MakeBudget(Guid userId, Guid? categoryId, long limitCents = 1_000_000) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CategoryId = categoryId,
        LimitCents = limitCents,
        PeriodType = BudgetPeriodType.Monthly,
    };

    [Fact]
    public async Task ApplyDeltaAsync_ZeroDelta_DoesNotTouchRepository()
    {
        await _service.ApplyDeltaAsync(Guid.NewGuid(), Guid.NewGuid(), 0, DateTimeOffset.UtcNow);

        _budgetRepository.Verify(
            r => r.GetMatchingBudgetsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyDeltaAsync_NoPeriodYet_CreatesPeriodWithBudgetLimitSnapshot()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var budget = MakeBudget(userId, categoryId, limitCents: 2_000_000);

        _budgetRepository.Setup(r => r.GetMatchingBudgetsAsync(userId, categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Budget> { budget });
        _budgetRepository.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BudgetPeriod?)null);

        BudgetPeriod? added = null;
        _budgetRepository.Setup(r => r.AddPeriod(It.IsAny<BudgetPeriod>()))
            .Callback<BudgetPeriod>(p => added = p);

        await _service.ApplyDeltaAsync(userId, categoryId, 75_000, new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

        added.Should().NotBeNull();
        added!.BudgetId.Should().Be(budget.Id);
        added.LimitCents.Should().Be(2_000_000);
        added.SpentCents.Should().Be(75_000);
    }

    [Fact]
    public async Task ApplyDeltaAsync_OneTransaction_UpdatesBothCategoryBudgetAndTotalBudget()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var categoryBudget = MakeBudget(userId, categoryId);
        var totalBudget = MakeBudget(userId, null);

        var categoryPeriod = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = categoryBudget.Id, SpentCents = 10_000 };
        var totalPeriod = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = totalBudget.Id, SpentCents = 50_000 };

        _budgetRepository.Setup(r => r.GetMatchingBudgetsAsync(userId, categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Budget> { categoryBudget, totalBudget });
        _budgetRepository.Setup(r => r.GetPeriodAsync(categoryBudget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(categoryPeriod);
        _budgetRepository.Setup(r => r.GetPeriodAsync(totalBudget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalPeriod);

        await _service.ApplyDeltaAsync(userId, categoryId, 25_000, DateTimeOffset.UtcNow);

        categoryPeriod.SpentCents.Should().Be(35_000);
        totalPeriod.SpentCents.Should().Be(75_000);
    }

    [Fact]
    public async Task ApplyDeltaAsync_ApplyThenRevert_ReturnsToOriginalSpent()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var budget = MakeBudget(userId, categoryId);
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, SpentCents = 40_000 };
        var transactedAt = DateTimeOffset.UtcNow;

        _budgetRepository.Setup(r => r.GetMatchingBudgetsAsync(userId, categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Budget> { budget });
        _budgetRepository.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(period);

        await _service.ApplyDeltaAsync(userId, categoryId, 15_000, transactedAt);
        await _service.ApplyDeltaAsync(userId, categoryId, -15_000, transactedAt);

        period.SpentCents.Should().Be(40_000);
    }

    [Fact]
    public async Task ApplyDeltaAsync_RevertLargerThanSpent_ClampsAtZero()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var budget = MakeBudget(userId, categoryId);
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, SpentCents = 5_000 };

        _budgetRepository.Setup(r => r.GetMatchingBudgetsAsync(userId, categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Budget> { budget });
        _budgetRepository.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(period);

        await _service.ApplyDeltaAsync(userId, categoryId, -50_000, DateTimeOffset.UtcNow);

        period.SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task ApplyDeltaAsync_UncategorizedTransaction_OnlyHitsTotalBudget()
    {
        var userId = Guid.NewGuid();
        var totalBudget = MakeBudget(userId, null);
        var totalPeriod = new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = totalBudget.Id, SpentCents = 0 };

        _budgetRepository.Setup(r => r.GetMatchingBudgetsAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Budget> { totalBudget });
        _budgetRepository.Setup(r => r.GetPeriodAsync(totalBudget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalPeriod);

        await _service.ApplyDeltaAsync(userId, null, 30_000, DateTimeOffset.UtcNow);

        totalPeriod.SpentCents.Should().Be(30_000);
    }

    [Theory]
    // 2026-09-30T18:00Z là 2026-10-01T01:00+07 — theo giờ VN đây là chi tiêu của tháng 10.
    [InlineData("2026-09-30T18:00:00+00:00", 2026, 10)]
    // 2026-10-01T16:00Z là 2026-10-01T23:00+07 — vẫn là tháng 10.
    [InlineData("2026-10-01T16:00:00+00:00", 2026, 10)]
    // 2026-09-30T16:59Z là 2026-09-30T23:59+07 — vẫn là tháng 9.
    [InlineData("2026-09-30T16:59:00+00:00", 2026, 9)]
    public void MonthRange_UsesVietnamOffsetForMonthBoundary(string instant, int expectedYear, int expectedMonth)
    {
        var (start, end) = VietnamTime.MonthRange(DateTimeOffset.Parse(instant));

        // Mốc trả về là 00:00 ngày 1 giờ VN, biểu diễn ở UTC (17:00 ngày cuối tháng trước).
        var vnStart = new DateTimeOffset(expectedYear, expectedMonth, 1, 0, 0, 0, TimeSpan.FromHours(7));
        start.Should().Be(vnStart);

        // So với mốc đầu tháng KẾ TIẾP theo giờ VN, không phải start.AddMonths(1): start giờ
        // là biểu diễn UTC nên cộng 1 tháng lên nó lệch khi 2 tháng khác số ngày.
        end.Should().Be(vnStart.AddMonths(1));

        VietnamTime.YearMonthOf(start).Should().Be((expectedYear, expectedMonth));
    }

    [Fact]
    public void MonthRange_ReturnsUtcOffset_BecauseNpgsqlRejectsNonZeroOffsets()
    {
        // Npgsql chỉ ghi được DateTimeOffset offset 0 vào cột `timestamp with time zone` —
        // kể cả khi giá trị chỉ dùng làm tham số truy vấn.
        var (start, end) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);

        start.Offset.Should().Be(TimeSpan.Zero);
        end.Offset.Should().Be(TimeSpan.Zero);
    }
}
