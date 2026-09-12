using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class RefreshTokenCommandHandler : IRefreshTokenCommandHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IAuditLogService _auditLogService;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        ITokenService tokenService,
        IAuditLogService auditLogService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _tokenService = tokenService;
        _auditLogService = auditLogService;
    }

    public async Task<AuthResultDto> HandleAsync(RefreshTokenCommand command, CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashToken(command.RefreshToken);
        var existing = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, ct);

        if (existing is null)
        {
            throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Refresh token không hợp lệ.");
        }

        if (existing.RevokedAt is not null)
        {
            // Token already rotated once before — this is a reuse, treat as a security breach.
            await _refreshTokenRepository.RevokeAllForUserAsync(existing.UserId, ct);
            await _auditLogService.LogAsync(
                AuditEvents.TokenReuseDetected, existing.UserId, command.IpAddress, ct: ct);
            throw new AuthenticationException(AuthErrorCodes.TokenReuseDetected, "Refresh token đã bị sử dụng lại.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new AuthenticationException(AuthErrorCodes.TokenExpired, "Refresh token đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(existing.UserId, ct)
            ?? throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Refresh token không hợp lệ.");

        var accessToken = _tokenService.GenerateAccessToken(user);
        var newRefresh = _tokenService.GenerateRefreshToken();

        var newToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefresh.TokenHash,
            ExpiresAt = newRefresh.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _refreshTokenRepository.RotateAsync(existing, newToken, ct);

        return new AuthResultDto(accessToken, newRefresh.RawToken, newRefresh.ExpiresAt);
    }
}
