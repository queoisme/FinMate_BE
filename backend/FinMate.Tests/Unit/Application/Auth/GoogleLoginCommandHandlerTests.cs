using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class GoogleLoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IGoogleTokenVerifier> _googleTokenVerifier = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly GoogleLoginCommandHandler _handler;

    public GoogleLoginCommandHandlerTests()
    {
        _handler = new GoogleLoginCommandHandler(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _googleTokenVerifier.Object,
            _tokenService.Object,
            _auditLogService.Object,
            new GoogleLoginCommandValidator());

        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokenService.Setup(t => t.GenerateRefreshToken())
            .Returns(new GeneratedRefreshToken("raw-refresh-token", "hashed-refresh-token", DateTimeOffset.UtcNow.AddDays(30)));
    }

    private static GoogleUserInfo VerifiedInfo(string sub = "google-sub-1", string email = "person@gmail.com", string? name = "Person") =>
        new(sub, email, true, name);

    [Fact]
    public async Task HandleAsync_InvalidToken_ThrowsAuthenticationException()
    {
        _googleTokenVerifier.Setup(v => v.VerifyAsync("bad-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleUserInfo?)null);

        var act = () => _handler.HandleAsync(new GoogleLoginCommand("bad-token", null));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenInvalid);
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_EmailNotVerified_ThrowsAuthenticationException()
    {
        _googleTokenVerifier.Setup(v => v.VerifyAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo("sub", "person@gmail.com", false, "Person"));

        var act = () => _handler.HandleAsync(new GoogleLoginCommand("token", null));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task HandleAsync_NewGoogleUser_CreatesUserWithoutPasswordAndIssuesTokens()
    {
        var info = VerifiedInfo();
        _googleTokenVerifier.Setup(v => v.VerifyAsync("token", It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _userRepository.Setup(r => r.GetByGoogleIdAsync(info.Sub, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _userRepository.Setup(r => r.GetByEmailAsync(info.Email, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        User? saved = null;
        _userRepository.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => saved = u)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(new GoogleLoginCommand("token", null));

        saved.Should().NotBeNull();
        saved!.PasswordHash.Should().BeNull();
        saved.GoogleId.Should().Be(info.Sub);
        saved.Email.Should().Be(info.Email);
        saved.DisplayName.Should().Be("Person");
        result.AccessToken.Should().Be("access-token");
        _userRepository.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingGoogleIdUser_LogsInWithoutCreatingDuplicate()
    {
        var info = VerifiedInfo();
        var existing = new User { Id = Guid.NewGuid(), Email = info.Email, GoogleId = info.Sub, DisplayName = "Person" };
        _googleTokenVerifier.Setup(v => v.VerifyAsync("token", It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _userRepository.Setup(r => r.GetByGoogleIdAsync(info.Sub, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _handler.HandleAsync(new GoogleLoginCommand("token", null));

        result.AccessToken.Should().Be("access-token");
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _userRepository.Verify(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ExistingPasswordAccountSameEmail_AutoLinksGoogleId()
    {
        var info = VerifiedInfo();
        var existing = new User
        {
            Id = Guid.NewGuid(),
            Email = info.Email,
            PasswordHash = "already-has-a-password",
            GoogleId = null,
            DisplayName = "Existing User",
        };
        _googleTokenVerifier.Setup(v => v.VerifyAsync("token", It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _userRepository.Setup(r => r.GetByGoogleIdAsync(info.Sub, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _userRepository.Setup(r => r.GetByEmailAsync(info.Email, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        User? updated = null;
        _userRepository.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => updated = u)
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(new GoogleLoginCommand("token", null));

        updated.Should().NotBeNull();
        updated!.GoogleId.Should().Be(info.Sub);
        updated.PasswordHash.Should().Be("already-has-a-password");
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_LockedAccount_ThrowsAuthenticationException()
    {
        var info = VerifiedInfo();
        var existing = new User { Id = Guid.NewGuid(), Email = info.Email, GoogleId = info.Sub, DisplayName = "Person", IsLocked = true };
        _googleTokenVerifier.Setup(v => v.VerifyAsync("token", It.IsAny<CancellationToken>())).ReturnsAsync(info);
        _userRepository.Setup(r => r.GetByGoogleIdAsync(info.Sub, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var act = () => _handler.HandleAsync(new GoogleLoginCommand("token", null));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.AccountLocked);
    }
}
