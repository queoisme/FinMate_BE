using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _handler = new RefreshTokenCommandHandler(
            _refreshTokenRepository.Object,
            _userRepository.Object,
            _tokenService.Object,
            _auditLogService.Object);

        _tokenService.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string raw) => $"hash-of-{raw}");
    }

    [Fact]
    public async Task HandleAsync_UnknownTokenHash_ThrowsTokenInvalid()
    {
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var act = () => _handler.HandleAsync(new RefreshTokenCommand("unknown"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenInvalid);
    }

    [Fact]
    public async Task HandleAsync_ExpiredToken_ThrowsTokenExpired()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = "hash-of-expired",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = null,
        };
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of-expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var act = () => _handler.HandleAsync(new RefreshTokenCommand("expired"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenExpired);
    }

    [Fact]
    public async Task HandleAsync_TokenAlreadyRotated_RevokesAllAndThrowsReuseDetected()
    {
        var userId = Guid.NewGuid();
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = "hash-of-reused",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplacedByTokenHash = "hash-of-the-token-that-replaced-it",
        };
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of-reused", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var act = () => _handler.HandleAsync(new RefreshTokenCommand("reused"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenReuseDetected);

        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _auditLogService.Verify(a => a.LogAsync(
            "Auth.TokenReuse.Detected", userId, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TokenRevokedWithoutReplacement_ThrowsTokenInvalid_WithNoReuseAlert()
    {
        // Thu hồi do đăng xuất mọi thiết bị / đổi mật khẩu / xóa tài khoản: không có token thay thế,
        // nên đây KHÔNG phải một lần dùng lại token đã bị lộ — không được báo động bảo mật.
        var userId = Guid.NewGuid();
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = "hash-of-logged-out",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplacedByTokenHash = null,
        };
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of-logged-out", It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var act = () => _handler.HandleAsync(new RefreshTokenCommand("logged-out"));

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.TokenInvalid);

        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogService.Verify(a => a.LogAsync(
            "Auth.TokenReuse.Detected", It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_RotatesAndReturnsNewPair()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "user@finmate.local", PasswordHash = "x", DisplayName = "U" };
        var oldToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = "hash-of-valid",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            RevokedAt = null,
        };
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync("hash-of-valid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _tokenService.Setup(t => t.GenerateAccessToken(user)).Returns("new-access-token");
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        _tokenService.Setup(t => t.GenerateRefreshToken())
            .Returns(new GeneratedRefreshToken("new-raw-token", "new-token-hash", expiresAt));

        var result = await _handler.HandleAsync(new RefreshTokenCommand("valid"));

        result.AccessToken.Should().Be("new-access-token");
        result.RefreshToken.Should().Be("new-raw-token");

        _refreshTokenRepository.Verify(r => r.RotateAsync(
            oldToken,
            It.Is<RefreshToken>(nt => nt.UserId == user.Id && nt.TokenHash == "new-token-hash"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
