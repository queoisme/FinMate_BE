using FinMate.Application.Auth.Otp;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Ops;

/// <summary>Bật hay tắt xác thực 2 lớp cho tài khoản nội bộ. Mặc định bật ngoài Development.</summary>
public sealed record OpsAuthOptions(bool RequireTwoFactor);

public static class OpsClaims
{
    /// <summary>Access token đã qua bước OTP. Thiếu thì mọi API nội bộ và <c>/admin/*</c> trả 403 khi xác thực 2 lớp đang bật.</summary>
    public const string Verified = "ops_verified";
    public const string VerifiedValue = "true";
}

public static class OpsTwoFactorRules
{
    /// <summary>Hết thời gian này phải nhập lại mã, dù refresh token còn hạn.</summary>
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(12);

    public static bool IsStillVerified(DateTimeOffset? verifiedAt, DateTimeOffset now)
        => verifiedAt is { } at && at + VerificationLifetime > now;
}

public record OpsTwoFactorStatusDto(bool Required, bool Verified);

public interface IOpsTwoFactorService
{
    Task SendCodeAsync(Guid actorId, CancellationToken ct = default);

    /// <summary>Đúng mã thì đánh dấu phiên của refresh token đó và trả access token mới có claim <see cref="OpsClaims.Verified"/>.</summary>
    Task<AuthResultDto> VerifyAsync(Guid actorId, string? code, string? refreshToken, string? ip, CancellationToken ct = default);
}

public sealed class OpsTwoFactorService : IOpsTwoFactorService
{
    private readonly IOpsAccessService _access;
    private readonly IOtpService _otp;
    private readonly IEmailSender _email;
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogService _audit;

    public OpsTwoFactorService(
        IOpsAccessService access,
        IOtpService otp,
        IEmailSender email,
        ITokenService tokens,
        IRefreshTokenRepository refreshTokens,
        IAuditLogService audit)
    {
        _access = access;
        _otp = otp;
        _email = email;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _audit = audit;
    }

    public async Task SendCodeAsync(Guid actorId, CancellationToken ct = default)
    {
        var user = await _access.RequireInternalAsync(actorId, ct);
        var issued = await _otp.IssueAsync(user.Email, OtpPurpose.OpsLogin, ct);

        // Đang trong thời gian chờ thì không gửi thêm: mã đã gửi nếu chưa dùng vẫn nhập được. Không báo lỗi để web chỉ cần một luồng.
        if (issued.Issued)
        {
            var (subject, body) = OtpMessages.OpsLogin(issued.Code!);
            if (!await _email.SendAsync(user.Email, subject, body, ct))
            {
                // Báo thẳng: mọi API nội bộ cần bước này, nên im lặng là cả đội bị khóa ngoài mà không biết vì sao.
                await _otp.ClearCooldownAsync(user.Email, OtpPurpose.OpsLogin, ct);
                throw new BusinessRuleException(OpsErrorCodes.OtpEmailFailed, "Không gửi được email chứa mã. Vui lòng thử lại hoặc báo Admin.");
            }
        }
    }

    public async Task<AuthResultDto> VerifyAsync(Guid actorId, string? code, string? refreshToken, string? ip, CancellationToken ct = default)
    {
        var user = await _access.RequireInternalAsync(actorId, ct);

        var session = string.IsNullOrWhiteSpace(refreshToken)
            ? null
            : await _refreshTokens.GetByTokenHashAsync(_tokens.HashToken(refreshToken), ct);
        if (session is null || session.UserId != user.Id || !session.IsActive)
        {
            throw new AuthenticationException(AuthErrorCodes.TokenInvalid, "Refresh token không hợp lệ.");
        }

        var result = await _otp.VerifyAsync(user.Email, OtpPurpose.OpsLogin, code ?? string.Empty, ct);
        if (result != OtpVerifyResult.Valid)
        {
            await _audit.LogAsync(AuditEvents.OpsTwoFactorFailed, user.Id, ip, ct: ct);
            throw result == OtpVerifyResult.TooManyAttempts
                ? new BusinessRuleException(AuthErrorCodes.OtpTooManyAttempts, "Bạn đã nhập sai quá nhiều lần. Vui lòng xin mã mới.")
                : new BusinessRuleException(AuthErrorCodes.OtpInvalid, "Mã không hợp lệ hoặc đã hết hạn.");
        }

        await _refreshTokens.MarkOpsVerifiedAsync(session, DateTimeOffset.UtcNow, ct);
        await _audit.LogAsync(AuditEvents.OpsTwoFactorVerified, user.Id, ip, ct: ct);

        return new AuthResultDto(_tokens.GenerateAccessToken(user, opsVerified: true), refreshToken!, session.ExpiresAt);
    }
}
