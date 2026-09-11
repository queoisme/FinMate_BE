using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

public class UpdateBudgetLimitCommandHandlerTests
{
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly UpdateBudgetLimitCommandHandler _handler;

    public UpdateBudgetLimitCommandHandlerTests()
    {
        _handler = new UpdateBudgetLimitCommandHandler(
            _budgetRepository.Object, _cache.Object, new UpdateBudgetLimitCommandValidator());
    }

    private (Budget Budget, BudgetPeriod Period) Setup(Guid userId, long limitCents, long spentCents)
    {
        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LimitCents = limitCents,
            PeriodType = BudgetPeriodType.Monthly,
        };
        var period = new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            LimitCents = limitCents,
            SpentCents = spentCents,
            Alert70SentAt = DateTimeOffset.UtcNow,
            Alert90SentAt = DateTimeOffset.UtcNow,
            Alert100SentAt = DateTimeOffset.UtcNow,
        };

        _budgetRepository.Setup(r => r.GetByIdAsync(budget.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(budget);
        _budgetRepository.Setup(r => r.GetPeriodAsync(budget.Id, VietnamTime.CurrentMonthStart(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(period);

        return (budget, period);
    }

    [Fact]
    public async Task HandleAsync_RaisesLimitAboveSpending_ResetsAllThreeAlertFlags()
    {
        var userId = Guid.NewGuid();
        var (budget, period) = Setup(userId, limitCents: 1_000_000, spentCents: 900_000);

        // Nâng lên 10 triệu: 900k chỉ còn 9% → cả 3 ngưỡng đều chưa chạm lại.
        await _handler.HandleAsync(new UpdateBudgetLimitCommand(userId, budget.Id, 10_000_000));

        budget.LimitCents.Should().Be(10_000_000);
        period.LimitCents.Should().Be(10_000_000);
        period.Alert70SentAt.Should().BeNull();
        period.Alert90SentAt.Should().BeNull();
        period.Alert100SentAt.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NewLimitStillBelowSpending_KeepsAlertFlags()
    {
        var userId = Guid.NewGuid();
        var (budget, period) = Setup(userId, limitCents: 1_000_000, spentCents: 1_500_000);

        await _handler.HandleAsync(new UpdateBudgetLimitCommand(userId, budget.Id, 1_200_000));

        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().NotBeNull();
        period.Alert100SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_RaisesLimitPastOverspendButStillAbove70Percent_ResetsOnlyTheClearedFlags()
    {
        var userId = Guid.NewGuid();
        var (budget, period) = Setup(userId, limitCents: 1_000_000, spentCents: 900_000);

        // 900k / 1tr trước là 90% → 900k / 1.05tr là ~85.7%: vẫn trên 70% nhưng đã tụt xuống
        // dưới 90% và không còn vượt limit. Mỗi mốc xét độc lập nên chỉ 90 và 100 được mở lại.
        await _handler.HandleAsync(new UpdateBudgetLimitCommand(userId, budget.Id, 1_050_000));

        period.Alert70SentAt.Should().NotBeNull();
        period.Alert90SentAt.Should().BeNull();
        period.Alert100SentAt.Should().BeNull();
    }
}
