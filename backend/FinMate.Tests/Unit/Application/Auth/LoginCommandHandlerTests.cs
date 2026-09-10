using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class LoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _passwordHasher.Object,
            _tokenService.Object,
            _auditLogService.Object,
            new LoginCommandValidator());
    }

    private static User MakeUser(bool isLocked = false) => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@finmate.local",
        PasswordHash = "hashed",
        DisplayName = "Test User",
        IsLocked = isLocked,
    };

    [Fact]
    public async Task HandleAsync_WrongPassword_ThrowsAuthenticationException()
    {
        var user = MakeUser();
        _userRepository.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("wrong", user.PasswordHash!)).Returns(false);

        var act = () => _handler.HandleAsync(new LoginCommand(user.Email, "wrong"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.InvalidCredentials);
        _tokenService.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_AccountLocked_ThrowsAuthenticationException()
    {
        var user = MakeUser(isLocked: true);
        _userRepository.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("correct", user.PasswordHash!)).Returns(true);

        var act = () => _handler.HandleAsync(new LoginCommand(user.Email, "correct"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.AccountLocked);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_IssuesTokensAndPersistsRefreshToken()
    {
        var user = MakeUser();
        _userRepository.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("correct", user.PasswordHash!)).Returns(true);
        _tokenService.Setup(t => t.GenerateAccessToken(user)).Returns("access-token");
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        _tokenService.Setup(t => t.GenerateRefreshToken())
            .Returns(new GeneratedRefreshToken("raw-refresh-token", "hashed-refresh-token", expiresAt));

        var result = await _handler.HandleAsync(new LoginCommand(user.Email, "correct"));

        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("raw-refresh-token");

        _refreshTokenRepository.Verify(r => r.AddAsync(
            It.Is<RefreshToken>(rt => rt.UserId == user.Id && rt.TokenHash == "hashed-refresh-token"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_GoogleOnlyAccountHasNoPasswordHash_ThrowsInvalidCredentialsNotCrash()
    {
        var user = MakeUser();
        user.PasswordHash = null;
        user.GoogleId = "google-sub-123";
        _userRepository.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var act = () => _handler.HandleAsync(new LoginCommand(user.Email, "anything"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.InvalidCredentials);
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
