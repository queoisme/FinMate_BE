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

    private static Category SystemCategory(bool active = true) => new()
    {
        Id = Guid.NewGuid(), UserId = null, IsSystem = true, IsActive = active, Name = "Ăn uống", Slug = "food",
    };

    [Fact]
    public async Task HandleAsync_AppearanceFields_ArePersistedAndColorIsUppercased()
    {
        var userId = Guid.NewGuid();
        var parent = SystemCategory();
        _categoryRepository.Setup(r => r.GetByIdAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        Category? saved = null;
        _categoryRepository
            .Setup(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var dto = await _handler.HandleAsync(new CreateCategoryCommand(
            userId, "Cà phê & Gặp gỡ", "coffee", Color: "#f0c8a0", Kind: "income", ParentCategoryId: parent.Id, SuggestedBudgetCents: 1_500_000, IsFavorite: true));

        saved!.Color.Should().Be("#F0C8A0");
        saved.Kind.Should().Be("income");
        saved.ParentCategoryId.Should().Be(parent.Id);
        saved.SuggestedBudgetCents.Should().Be(1_500_000);
        saved.IsFavorite.Should().BeTrue();
        dto.Color.Should().Be("#F0C8A0");
        dto.Kind.Should().Be("income");
        dto.ParentCategoryId.Should().Be(parent.Id);
        dto.SuggestedBudgetCents.Should().Be(1_500_000);
        dto.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoAppearanceFields_UsesDefaults()
    {
        Category? saved = null;
        _categoryRepository
            .Setup(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Mặc định", null, SuggestedBudgetCents: 0));

        saved!.Color.Should().BeNull();
        saved.Kind.Should().Be("expense");
        saved.ParentCategoryId.Should().BeNull();
        saved.SuggestedBudgetCents.Should().BeNull("hạn mức 0 nghĩa là không đặt hạn mức gợi ý");
        saved.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ParentThatIsNotASystemCategory_IsRejected()
    {
        var notAllowed = new[]
        {
            new Category { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsSystem = false, IsActive = true, Name = "Riêng", Slug = "rieng" },
            SystemCategory(active: false),
        };
        foreach (var parent in notAllowed)
        {
            _categoryRepository.Setup(r => r.GetByIdAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

            var act = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Con", null, ParentCategoryId: parent.Id));

            await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == CategoryErrorCodes.ParentInvalid);
        }

        var missing = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Con", null, ParentCategoryId: Guid.NewGuid()));
        await missing.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == CategoryErrorCodes.ParentInvalid);
        _categoryRepository.Verify(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("F0C8A0")]
    public async Task HandleAsync_InvalidColor_ThrowsValidationException(string color)
    {
        var act = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Màu sai", null, Color: color));

        await act.Should().ThrowAsync<ValidationException>();
        _categoryRepository.Verify(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InvalidKindOrBudget_ThrowsValidationException()
    {
        var badKind = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Loại sai", null, Kind: "transfer"));
        var negative = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Âm", null, SuggestedBudgetCents: -1));
        var tooBig = () => _handler.HandleAsync(new CreateCategoryCommand(Guid.NewGuid(), "Quá lớn", null, SuggestedBudgetCents: 100_000_000_001));

        await badKind.Should().ThrowAsync<ValidationException>();
        await negative.Should().ThrowAsync<ValidationException>();
        await tooBig.Should().ThrowAsync<ValidationException>();
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
