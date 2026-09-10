using FinMate.Application.Categories.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Categories;

public class DeleteCategoryCommandHandlerTests
{
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly DeleteCategoryCommandHandler _handler;

    public DeleteCategoryCommandHandlerTests()
    {
        _handler = new DeleteCategoryCommandHandler(_categoryRepository.Object);
    }

    [Fact]
    public async Task HandleAsync_NotOwned_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(categoryId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var act = () => _handler.HandleAsync(new DeleteCategoryCommand(userId, categoryId));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_Owned_SoftDeletes()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), UserId = userId, Name = "X", Slug = "x" };
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(category.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        await _handler.HandleAsync(new DeleteCategoryCommand(userId, category.Id));

        category.DeletedAt.Should().NotBeNull();
        _categoryRepository.Verify(r => r.UpdateAsync(category, It.IsAny<CancellationToken>()), Times.Once);
    }
}
