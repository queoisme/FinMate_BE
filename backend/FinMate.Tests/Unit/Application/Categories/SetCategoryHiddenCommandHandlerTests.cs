using FinMate.Application.Categories.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Categories;

public class SetCategoryHiddenCommandHandlerTests
{
    private readonly Mock<ICategoryRepository> _repo = new();
    private readonly Guid _userId = Guid.NewGuid();

    private Category Setup(string slug, bool system = true, bool active = true)
    {
        var c = new Category { Id = Guid.NewGuid(), Slug = slug, IsSystem = system, IsActive = active, UserId = system ? null : _userId };
        _repo.Setup(r => r.GetByIdAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    [Fact]
    public async Task Hide_SystemCategory_Persists()
    {
        var c = Setup("food");
        await new SetCategoryHiddenCommandHandler(_repo.Object).HandleAsync(new(_userId, c.Id, true));
        _repo.Verify(r => r.SetHiddenAsync(_userId, c.Id, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hide_FallbackOther_Rejected_ButUnhideAllowed()
    {
        var c = Setup("other");
        var h = new SetCategoryHiddenCommandHandler(_repo.Object);
        await ((Func<Task>)(() => h.HandleAsync(new(_userId, c.Id, true)))).Should().ThrowAsync<BusinessRuleException>();
        await h.HandleAsync(new(_userId, c.Id, false));
        _repo.Verify(r => r.SetHiddenAsync(_userId, c.Id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hide_OwnCustomCategory_NotFound()
    {
        var c = Setup("ca-phe", system: false);
        await ((Func<Task>)(() => new SetCategoryHiddenCommandHandler(_repo.Object).HandleAsync(new(_userId, c.Id, true)))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task VisibleBySlug_HiddenForUser_ReturnsNull()
    {
        var c = Setup("food");
        _repo.Setup(r => r.GetSystemBySlugAsync("food", It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _repo.Setup(r => r.IsHiddenAsync(_userId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await _repo.Object.GetVisibleSystemBySlugAsync(_userId, "food")).Should().BeNull();
        (await _repo.Object.GetVisibleSystemBySlugAsync(Guid.NewGuid(), "food")).Should().Be(c);
    }
}
