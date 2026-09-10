using FinMate.Application.Categories.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Categories;

public class CreateCategoryCommandHandlerTests
{
    private readonly Mock<ICategoryRepository> _categoryRepository = new();
    private readonly CreateCategoryCommandHandler _handler;

    public CreateCategoryCommandHandlerTests()
    {
        _handler = new CreateCategoryCommandHandler(_categoryRepository.Object, new CreateCategoryCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_DuplicateSlugForSameUser_ThrowsConflictException()
    {
        var userId = Guid.NewGuid();
        _categoryRepository
            .Setup(r => r.ExistsBySlugAsync(userId, "an-uong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateCategoryCommand(userId, "Ăn uống", null);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == CategoryErrorCodes.SlugDuplicate);
        _categoryRepository.Verify(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_SlugifiesVietnameseNameAndPersists()
    {
        var userId = Guid.NewGuid();
        Category? saved = null;
        _categoryRepository
            .Setup(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var command = new CreateCategoryCommand(userId, "Đi chợ & Siêu thị", "shopping_cart");

        var dto = await _handler.HandleAsync(command);

        saved.Should().NotBeNull();
        saved!.Slug.Should().Be("di-cho-sieu-thi");
        saved.UserId.Should().Be(userId);
        saved.IsSystem.Should().BeFalse();
        dto.Slug.Should().Be("di-cho-sieu-thi");
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsValidationExceptionAndDoesNotPersist()
    {
        var command = new CreateCategoryCommand(Guid.NewGuid(), "", null);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
        _categoryRepository.Verify(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
