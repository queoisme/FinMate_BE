using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class UpdateUserProfileCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly UpdateUserProfileCommandHandler _handler;
    private readonly User _user = new() { Id = Guid.NewGuid(), Email = "a@finmate.local", DisplayName = "A" };

    public UpdateUserProfileCommandHandlerTests()
    {
        _userRepository.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _handler = new UpdateUserProfileCommandHandler(
            _userRepository.Object, _cache.Object, new UpdateUserProfileCommandValidator());
    }

    [Fact]
    public void NewUser_DefaultsToNotSharing()
    {
        new User().ShareReceiptItemsWithAi.Should().BeFalse();
    }

    [Fact]
    public async Task Patch_WithTrue_PersistsAndReturnsIt()
    {
        var dto = await _handler.HandleAsync(new UpdateUserProfileCommand(_user.Id, "A", null, true));

        dto.ShareReceiptItemsWithAi.Should().BeTrue();
        _user.ShareReceiptItemsWithAi.Should().BeTrue();
        _userRepository.Verify(r => r.UpdateAsync(_user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Patch_WithoutTheField_LeavesItUnchanged()
    {
        await _handler.HandleAsync(new UpdateUserProfileCommand(_user.Id, "A", null, true));

        var dto = await _handler.HandleAsync(new UpdateUserProfileCommand(_user.Id, "Renamed"));

        dto.ShareReceiptItemsWithAi.Should().BeTrue();
        _user.ShareReceiptItemsWithAi.Should().BeTrue();
    }

    [Fact]
    public async Task Patch_WithFalse_TurnsItOff()
    {
        await _handler.HandleAsync(new UpdateUserProfileCommand(_user.Id, "A", null, true));

        var dto = await _handler.HandleAsync(new UpdateUserProfileCommand(_user.Id, "A", null, false));

        dto.ShareReceiptItemsWithAi.Should().BeFalse();
        _user.ShareReceiptItemsWithAi.Should().BeFalse();
    }
}
