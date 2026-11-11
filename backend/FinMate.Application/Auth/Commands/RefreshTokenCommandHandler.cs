using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

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
            // ReplacedByTokenHash chỉ được đặt khi RotateAsync thực sự xoay token (xem RefreshTokenRepository.RotateAsync).
            // Có giá trị đó nghĩa là token này đã bị dùng để đổi lấy token mới trước đây — ai đó đang trình lại một token
            // đã bị thay thế, tức token bị lộ. Null nghĩa là bị thu hồi vì lý do khác (đăng xuất mọi thiết bị, đổi mật
            // khẩu, xóa tài khoản) — hợp lệ và chủ động từ người dùng, không phải tấn công.
            if (existing.ReplacedByTokenHash is not null)
            {
                await _refreshTokenRepository.RevokeAllForUserAsync(existing.UserId, ct);
                await _auditLogService.LogAsync(
                    AuditEvents.TokenReuseDetected, existing.UserId, command.IpAddress, ct: ct);
                throw new AuthenticationException(AuthErrorCodes.TokenReuseDetected, "Refresh token đã bị sử dụng lại.");
            }

            throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Refresh token không hợp lệ.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new AuthenticationException(AuthErrorCodes.TokenExpired, "Refresh token đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(existing.UserId, ct)
            ?? throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Refresh token không hợp lệ.");

        // Gia hạn không phải nhập lại mã, nhưng không vượt quá thời hạn tính từ lần nhập thật.
        var now = DateTimeOffset.UtcNow;
        var opsVerified = user.Role.IsInternal() && OpsTwoFactorRules.IsStillVerified(existing.OpsVerifiedAt, now);
        var accessToken = opsVerified ? _tokenService.GenerateAccessToken(user, opsVerified: true) : _tokenService.GenerateAccessToken(user);
        var newRefresh = _tokenService.GenerateRefreshToken();

        var newToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefresh.TokenHash,
            ExpiresAt = newRefresh.ExpiresAt,
            CreatedAt = now,
            OpsVerifiedAt = opsVerified ? existing.OpsVerifiedAt : null,
        };

        await _refreshTokenRepository.RotateAsync(existing, newToken, ct);

        return new AuthResultDto(accessToken, newRefresh.RawToken, newRefresh.ExpiresAt);
    }
}
