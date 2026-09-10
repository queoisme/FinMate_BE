using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

public class CreateBudgetCommandHandlerTests
{
    private readonly Mock<IBudgetRepository> _budgetRepository = new();
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CreateBudgetCommandHandler _handler;

    public CreateBudgetCommandHandlerTests()
    {
        _handler = new CreateBudgetCommandHandler(
            _budgetRepository.Object,
            _categoryRepository.Object,
            _transactionRepository.Object,
            _cache.Object,
            new CreateBudgetCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_DuplicateBudget_ThrowsConflictException()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };

        _categoryRepository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _budgetRepository.Setup(r => r.ExistsAsync(userId, category.Id, BudgetPeriodType.Monthly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _handler.HandleAsync(new CreateBudgetCommand(userId, category.Id, 1_000_000));

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.ErrorCode.Should().Be(BudgetErrorCodes.AlreadyExists);
    }

    [Fact]
    public async Task HandleAsync_CategoryOfAnotherUser_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Slug = "x", Name = "X" };

        _categoryRepository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var act = () => _handler.HandleAsync(new CreateBudgetCommand(userId, category.Id, 1_000_000));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_CreatedMidMonth_BackfillsPeriodWithAlreadySpentAmount()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống" };

        _categoryRepository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _budgetRepository.Setup(r => r.ExistsAsync(userId, category.Id, BudgetPeriodType.Monthly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _transactionRepository.Setup(r => r.SumConfirmedSpendAsync(
                userId, category.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(320_000);

        BudgetPeriod? added = null;
        _budgetRepository.Setup(r => r.AddPeriod(It.IsAny<BudgetPeriod>()))
            .Callback<BudgetPeriod>(p => added = p);

        var result = await _handler.HandleAsync(new CreateBudgetCommand(userId, category.Id, 1_000_000));

        added.Should().NotBeNull();
        added!.SpentCents.Should().Be(320_000);
        added.LimitCents.Should().Be(1_000_000);
        result.CategorySlug.Should().Be("food");
        result.PeriodType.Should().Be("monthly");
    }

    [Fact]
    public async Task HandleAsync_TotalBudget_DoesNotLookUpCategory()
    {
        var userId = Guid.NewGuid();

        _budgetRepository.Setup(r => r.ExistsAsync(userId, null, BudgetPeriodType.Monthly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _transactionRepository.Setup(r => r.SumConfirmedSpendAsync(
                userId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.HandleAsync(new CreateBudgetCommand(userId, null, 5_000_000));

        result.CategoryId.Should().BeNull();
        _categoryRepository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
