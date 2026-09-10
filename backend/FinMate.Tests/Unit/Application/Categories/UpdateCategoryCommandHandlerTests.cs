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
}
