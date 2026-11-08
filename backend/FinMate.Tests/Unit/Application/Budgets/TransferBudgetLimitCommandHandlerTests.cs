using FinMate.Application.Budgets;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

public class TransferBudgetLimitCommandHandlerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IBudgetRepository> _budgets = new();
    private readonly Mock<IAlertThresholdProvider> _thresholds = new();
    private readonly TransferBudgetLimitCommandHandler _handler;

    public TransferBudgetLimitCommandHandlerTests()
    {
        _thresholds.Setup(t => t.GetAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new AlertThresholds(70, 90));
        _handler = new TransferBudgetLimitCommandHandler(_budgets.Object, new Mock<ICacheService>().Object, new TransferBudgetLimitCommandValidator(), _thresholds.Object);
    }

    private (Budget Budget, BudgetPeriod Period) Stored(long limit, long spent, bool total = false)
    {
        var budget = new Budget { Id = Guid.NewGuid(), UserId = _userId, CategoryId = total ? null : Guid.NewGuid(), LimitCents = limit };
        var period = new BudgetPeriod { BudgetId = budget.Id, LimitCents = limit, SpentCents = spent };
        _budgets.Setup(r => r.GetByIdAsync(budget.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(budget);
        _budgets.Setup(r => r.GetPeriodAsync(budget.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(period);
        return (budget, period);
    }

    [Fact]
    public async Task MovesLimit_OnBothTheBudgetsAndTheirCurrentPeriods()
    {
        var (from, fromPeriod) = Stored(3_000_000, 1_000_000);
        var (to, toPeriod) = Stored(1_000_000, 900_000);

        var result = await _handler.HandleAsync(new TransferBudgetLimitCommand(_userId, from.Id, to.Id, 500_000));

        result.From.LimitCents.Should().Be(2_500_000);
        result.To.LimitCents.Should().Be(1_500_000);
        fromPeriod.LimitCents.Should().Be(2_500_000);
        toPeriod.LimitCents.Should().Be(1_500_000);
    }

    [Fact]
    public async Task MoreThanTheUnspentPart_IsRejected()
    {
        var (from, _) = Stored(3_000_000, 2_800_000);
        var (to, _) = Stored(1_000_000, 0);

        var act = () => _handler.HandleAsync(new TransferBudgetLimitCommand(_userId, from.Id, to.Id, 300_000));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(BudgetErrorCodes.TransferExceedsUnspent);
    }

    [Fact]
    public async Task TheOverallBudget_CannotTakePart()
    {
        var (from, _) = Stored(10_000_000, 0, total: true);
        var (to, _) = Stored(1_000_000, 0);

        var act = () => _handler.HandleAsync(new TransferBudgetLimitCommand(_userId, from.Id, to.Id, 100_000));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(BudgetErrorCodes.TransferNeedsCategoryBudgets);
    }

    [Fact]
    public async Task SomeoneElsesBudget_IsNotFound()
    {
        var (from, _) = Stored(3_000_000, 0);

        var act = () => _handler.HandleAsync(new TransferBudgetLimitCommand(_userId, from.Id, Guid.NewGuid(), 100_000));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
