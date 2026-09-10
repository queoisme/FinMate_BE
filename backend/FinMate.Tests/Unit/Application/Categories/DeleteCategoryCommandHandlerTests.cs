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
    private readonly Mock<ITransactionRepository> _transactionRepository = new();
    private readonly DeleteCategoryCommandHandler _handler;

    public DeleteCategoryCommandHandlerTests()
    {
        _handler = new DeleteCategoryCommandHandler(_categoryRepository.Object, _transactionRepository.Object);
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
    public async Task HandleAsync_CategoryHasTransactions_ThrowsConflictExceptionAndDoesNotDelete()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), UserId = userId, Name = "X", Slug = "x" };
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(category.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _transactionRepository
            .Setup(r => r.HasAnyForCategoryAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _handler.HandleAsync(new DeleteCategoryCommand(userId, category.Id));

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == CategoryErrorCodes.HasTransactions);
        _categoryRepository.Verify(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OwnedWithoutTransactions_SoftDeletes()
    {
        var userId = Guid.NewGuid();
        var category = new Category { Id = Guid.NewGuid(), UserId = userId, Name = "X", Slug = "x" };
        _categoryRepository
            .Setup(r => r.GetOwnedByUserAsync(category.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        _transactionRepository
            .Setup(r => r.HasAnyForCategoryAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _handler.HandleAsync(new DeleteCategoryCommand(userId, category.Id));

        category.DeletedAt.Should().NotBeNull();
        _categoryRepository.Verify(r => r.UpdateAsync(category, It.IsAny<CancellationToken>()), Times.Once);
    }
}
