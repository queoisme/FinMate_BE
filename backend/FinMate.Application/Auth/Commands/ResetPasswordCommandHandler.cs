using FinMate.Application.Auth.Otp;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class ResetPasswordCommandHandler : IResetPasswordCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IOtpService _otpService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<ResetPasswordCommand> _validator;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IOtpService otpService,
        IPasswordHasher passwordHasher,
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogService auditLogService,
        IValidator<ResetPasswordCommand> validator)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _passwordHasher = passwordHasher;
        _refreshTokenRepository = refreshTokenRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var result = await _otpService.VerifyAsync(
            command.Email, OtpPurpose.ResetPassword, command.Code, ct);
        if (result != OtpVerifyResult.Valid)
        {
            throw VerifyEmailCommandHandler.Failure(result);
        }

        var user = await _userRepository.GetByEmailAsync(command.Email, ct)
            ?? throw new BusinessRuleException(
                AuthErrorCodes.OtpInvalid, "Mã không hợp lệ hoặc đã hết hạn.");

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = _passwordHasher.Hash(command.NewPassword);
        user.UpdatedAt = now;

        // Đặt lại mật khẩu cũng xác minh luôn quyền sở hữu email — họ vừa đọc được mã gửi tới
        // đó. Bắt làm lại một lần nữa là thừa.
        user.EmailVerifiedAt ??= now;

        await _userRepository.UpdateAsync(user, ct);

        // Thu hồi mọi phiên đang mở. Lý do đặt lại mật khẩu thường là "tôi nghĩ có người vào
        // được tài khoản" — để phiên cũ sống tiếp là không giải quyết được đúng điều đó.
        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);

        await _auditLogService.LogAsync(AuditEvents.PasswordReset, user.Id, ct: ct);
    }
}
