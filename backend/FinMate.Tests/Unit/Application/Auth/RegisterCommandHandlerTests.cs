using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class RegisterCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        _handler = new RegisterCommandHandler(
            _userRepository.Object, _passwordHasher.Object, _auditLogService.Object, new RegisterCommandValidator());
    }

    [Fact]
    public async Task HandleAsync_EmailAlreadyExists_ThrowsConflictException()
    {
        _userRepository.Setup(r => r.ExistsByEmailAsync("taken@finmate.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new RegisterCommand("taken@finmate.local", "Password123!", "Test User");

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.EmailAlreadyExists);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NewEmail_HashesPasswordAndPersistsUser()
    {
        _userRepository.Setup(r => r.ExistsByEmailAsync("new@finmate.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _passwordHasher.Setup(h => h.Hash("Password123!")).Returns("hashed-password");

        User? savedUser = null;
        _userRepository.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => savedUser = u)
            .Returns(Task.CompletedTask);

        var command = new RegisterCommand("new@finmate.local", "Password123!", "Test User");

        var profile = await _handler.HandleAsync(command);

        savedUser.Should().NotBeNull();
        savedUser!.PasswordHash.Should().Be("hashed-password");
        savedUser.PasswordHash.Should().NotBe("Password123!");
        savedUser.Email.Should().Be("new@finmate.local");

        profile.Email.Should().Be("new@finmate.local");
        profile.DisplayName.Should().Be("Test User");

        _auditLogService.Verify(a => a.LogAsync(
            "Auth.User.Registered", savedUser.Id, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("not-an-email", "Password123!", "Test User")]
    [InlineData("valid@finmate.local", "short1", "Test User")]
    [InlineData("valid@finmate.local", "Password123!", "")]
    public async Task HandleAsync_InvalidFormat_ThrowsValidationExceptionAndDoesNotPersist(
        string email, string password, string displayName)
    {
        var command = new RegisterCommand(email, password, displayName);

        var act = () => _handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
