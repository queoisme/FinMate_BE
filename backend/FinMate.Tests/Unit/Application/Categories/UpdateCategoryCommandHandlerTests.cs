using FinMate.Application.Categories.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Categories;

public class UpdateCategoryCommandHandlerTests
{
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly UpdateCategoryCommandHandler _handler;

    public UpdateCategoryCommandHandlerTests()
    {
        _handler = new UpdateCategoryCommandHandler(_categoryRepository.Object, new UpdateCategoryCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_SystemCategory_ThrowsNotFoundBecauseRepositoryOnlyReturnsOwned()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(categoryId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var command = new UpdateCategoryCommand(userId, categoryId, "Ăn uống mới", null);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_OwnedCategory_UpdatesNameAndIconWithoutChangingSlug()
    {
        var userId = Guid.NewGuid();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Cũ",
            Slug = "cu",
            IconName = "old_icon",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(category.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var command = new UpdateCategoryCommand(userId, category.Id, "Mới", "new_icon");

        var dto = await _handler.HandleAsync(command);

        dto.Name.Should().Be("Mới");
        dto.IconName.Should().Be("new_icon");
        dto.Slug.Should().Be("cu");
        _categoryRepository.Verify(r => r.UpdateAsync(category, It.IsAny<CancellationToken>()), Times.Once);
    }

    private (Category Category, Category Parent) OwnedWithAppearance(Guid userId)
    {
        var parent = new Category { Id = Guid.NewGuid(), IsSystem = true, IsActive = true, Name = "Ăn uống", Slug = "food" };
        var category = new Category
        {
            Id = Guid.NewGuid(), UserId = userId, Name = "Cũ", Slug = "cu", IconName = "coffee",
            Color = "#8FB3E0", Kind = "expense", ParentCategoryId = parent.Id, SuggestedBudgetCents = 500_000, IsFavorite = true,
        };
        _categoryRepository.Setup(r => r.GetOwnedByUserAsync(category.Id, userId, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        _categoryRepository.Setup(r => r.GetByIdAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        return (category, parent);
    }

    [Fact]
    public async Task HandleAsync_OldClientSendingOnlyNameAndIcon_KeepsColorGroupBudgetAndFavorite()
    {
        var userId = Guid.NewGuid();
        var (category, parent) = OwnedWithAppearance(userId);

        var dto = await _handler.HandleAsync(new UpdateCategoryCommand(userId, category.Id, "Mới", "new_icon"));

        dto.Color.Should().Be("#8FB3E0");
        dto.Kind.Should().Be("expense");
        dto.ParentCategoryId.Should().Be(parent.Id);
        dto.SuggestedBudgetCents.Should().Be(500_000);
        dto.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SetsNewValuesAndNormalizesColor()
    {
        var userId = Guid.NewGuid();
        var (category, _) = OwnedWithAppearance(userId);

        var dto = await _handler.HandleAsync(new UpdateCategoryCommand(
            userId, category.Id, "Mới", "new_icon", Color: "#d5cbf5", Kind: "income", SuggestedBudgetCents: 2_000_000, IsFavorite: false));

        dto.Color.Should().Be("#D5CBF5");
        dto.Kind.Should().Be("income");
        dto.SuggestedBudgetCents.Should().Be(2_000_000);
        dto.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_EmptyGuidClearsTheParentAndZeroClearsTheBudget()
    {
        var userId = Guid.NewGuid();
        var (category, _) = OwnedWithAppearance(userId);

        var dto = await _handler.HandleAsync(new UpdateCategoryCommand(userId, category.Id, "Mới", null, ParentCategoryId: Guid.Empty, SuggestedBudgetCents: 0));

        dto.ParentCategoryId.Should().BeNull();
        dto.SuggestedBudgetCents.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NewParentMustBeASystemCategory()
    {
        var userId = Guid.NewGuid();
        var (category, _) = OwnedWithAppearance(userId);
        var other = new Category { Id = Guid.NewGuid(), UserId = userId, IsSystem = false, IsActive = true, Name = "Riêng", Slug = "rieng" };
        _categoryRepository.Setup(r => r.GetByIdAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var act = () => _handler.HandleAsync(new UpdateCategoryCommand(userId, category.Id, "Mới", null, ParentCategoryId: other.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == CategoryErrorCodes.ParentInvalid);
    }
}
