using FinMate.Application.Budgets;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

/// <summary>
/// Cảnh báo ngưỡng phải bắn NGAY khi giao dịch phát sinh (docx Flow 2 mục 2a, Flow 3 mục 1),
/// không phải chờ job theo giờ. Trước Phase 10 người dùng vượt 90% hạn mức có thể chờ tới 59
/// phút mới thấy Mascot phản ứng.
/// </summary>
public class BudgetPeriodServiceAlertTests
{
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly Mock<IBudgetAlertNotifier> _alertNotifier = new();
    private readonly BudgetPeriodService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    public BudgetPeriodServiceAlertTests()
    {
        _alertNotifier
            .Setup(n => n.IsEnabledAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _service = new BudgetPeriodService(_budgetRepository.Object, _alertNotifier.Object);
    }

    private BudgetPeriod Arrange(long limitCents, long alreadySpentCents, string categoryName = "Ăn uống")
    {
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = _categoryId,
            Category = new Category { Id = _categoryId, Name = categoryName, Slug = "food" },
            LimitCents = limitCents,
        };
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            LimitCents = limitCents,
            SpentCents = alreadySpentCents,
        };

        _budgetRepository
            .Setup(r => r.GetMatchingBudgetsAsync(_userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([budget]);
        _budgetRepository
            .Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(period);

        return period;
    }

    private Task<IReadOnlyList<BudgetAlert>> SpendAsync(long amountCents)
        => _service.ApplyDeltaAsync(_userId, _categoryId, amountCents, DateTimeOffset.UtcNow);

    [Fact]
    public async Task CrossingSeventyPercentAlertsOnTheTransactionItself()
    {
        Arrange(limitCents: 1_000_000, alreadySpentCents: 0);

        var alerts = await SpendAsync(750_000);

        alerts.Should().ContainSingle();
        alerts[0].Percent.Should().Be(70);
        alerts[0].UserId.Should().Be(_userId);
        alerts[0].Body.Should().Contain("Ăn uống", "tên danh mục là phần khiến cảnh báo có nghĩa");
    }

    [Fact]
    public async Task StayingBelowSeventyPercentAlertsNothing()
    {
        Arrange(limitCents: 1_000_000, alreadySpentCents: 0);

        (await SpendAsync(500_000)).Should().BeEmpty();
    }

    [Fact]
    public async Task EachThresholdAlertsExactlyOnce()
    {
        var period = Arrange(limitCents: 1_000_000, alreadySpentCents: 0);

        (await SpendAsync(750_000)).Should().ContainSingle().Which.Percent.Should().Be(70);
        (await SpendAsync(50_000)).Should().BeEmpty("vẫn ở vùng 70%, không có mốc mới");
        (await SpendAsync(150_000)).Should().ContainSingle().Which.Percent.Should().Be(90);
        (await SpendAsync(100_000)).Should().ContainSingle().Which.Percent.Should().Be(100);
        (await SpendAsync(500_000)).Should().BeEmpty("đã vượt 100%, không còn mốc nào để báo");

        period.SpentCents.Should().Be(1_550_000);
    }

    [Fact]
    public async Task OneBigTransactionCrossingAllThreeThresholdsAlertsOnlyTheHighest()
    {
        // Mua một món hết 120% hạn mức: gửi cả ba thông báo là spam, mà gửi mốc 70% sau khi
        // đã vượt 100% thì sai hẳn thông điệp.
        var period = Arrange(limitCents: 1_000_000, alreadySpentCents: 0);

        var alerts = await SpendAsync(1_200_000);

        alerts.Should().ContainSingle();
        alerts[0].Percent.Should().Be(100);

        // Hai mốc thấp bị đóng lại để lần sau không bắn ngược.
        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefundsNeverAlert()
    {
        // Sửa hoặc xoá giao dịch sinh delta âm; phần trăm chỉ có thể đi xuống.
        Arrange(limitCents: 1_000_000, alreadySpentCents: 950_000);

        var alerts = await _service.ApplyDeltaAsync(
            _userId, _categoryId, -500_000, DateTimeOffset.UtcNow);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task ABudgetWithoutACategoryReportsTheWholeSpendScope()
    {
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = null,
            LimitCents = 1_000_000,
        };
        _budgetRepository
            .Setup(r => r.GetMatchingBudgetsAsync(_userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([budget]);
        _budgetRepository
            .Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = budget.Id, LimitCents = 1_000_000 });

        var alerts = await SpendAsync(900_000);

        alerts.Should().ContainSingle().Which.Body.Should().Contain("toàn bộ chi tiêu");
    }

    [Fact]
    public async Task ATransactionTouchingTwoBudgetsCanRaiseTwoAlerts()
    {
        // Một giao dịch tiêu hạn mức của cả budget danh mục lẫn budget tổng.
        var categoryBudget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = _categoryId,
            Category = new Category { Id = _categoryId, Name = "Ăn uống", Slug = "food" },
            LimitCents = 1_000_000,
        };
        var totalBudget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = null,
            LimitCents = 1_000_000,
        };

        _budgetRepository
            .Setup(r => r.GetMatchingBudgetsAsync(_userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([categoryBudget, totalBudget]);
        foreach (var budget in new[] { categoryBudget, totalBudget })
        {
            var id = budget.Id;
            _budgetRepository
                .Setup(r => r.GetPeriodAsync(id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BudgetPeriod { Id = Guid.NewGuid(), BudgetId = id, LimitCents = 1_000_000 });
        }

        var alerts = await SpendAsync(950_000);

        alerts.Should().HaveCount(2);
        alerts.Select(a => a.Percent).Should().AllBeEquivalentTo(90);
    }

    [Fact]
    public async Task TurningAlertsOffLeavesTheThresholdUnspentForLater()
    {
        // Tắt thông báo không được ĂN mất mốc. Nếu đánh dấu Alert70SentAt lúc này, người dùng
        // bật lại vào ngày mai sẽ không bao giờ nhận cảnh báo cho mốc đã trót vượt.
        _alertNotifier
            .Setup(n => n.IsEnabledAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var period = Arrange(limitCents: 1_000_000, alreadySpentCents: 0);

        (await SpendAsync(750_000)).Should().BeEmpty();

        period.SpentCents.Should().Be(750_000, "số tiền vẫn phải được cộng dồn");
        period.Alert70SentAt.Should().BeNull();
    }

    [Fact]
    public async Task AZeroLimitBudgetDoesNotDivideByZero()
    {
        Arrange(limitCents: 0, alreadySpentCents: 0);

        (await SpendAsync(100_000)).Should().BeEmpty();
    }
}
