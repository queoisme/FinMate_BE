using FinMate.Application.Auth.Otp;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class VerifyEmailCommandHandler : IVerifyEmailCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IOtpService _otpService;
    private readonly IAuditLogService _auditLogService;

    public VerifyEmailCommandHandler(
        IUserRepository userRepository, IOtpService otpService, IAuditLogService auditLogService)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _auditLogService = auditLogService;
    }

    public async Task HandleAsync(VerifyEmailCommand command, CancellationToken ct = default)
    {
        // Kiểm mã TRƯỚC khi tra người dùng: làm ngược lại thì thời gian phản hồi khác nhau
        // giữa email có thật và email bịa, tức là vẫn dò được tài khoản.
        var result = await _otpService.VerifyAsync(command.Email, OtpPurpose.VerifyEmail, command.Code, ct);
        if (result != OtpVerifyResult.Valid)
        {
            throw Failure(result);
        }

        var user = await _userRepository.GetByEmailAsync(command.Email, ct);
        if (user is null)
        {
            // Mã đúng cho một email không còn tồn tại — chỉ xảy ra khi tài khoản bị xoá giữa
            // lúc gửi mã và lúc nhập. Vẫn trả lỗi chung.
            throw new BusinessRuleException(AuthErrorCodes.OtpInvalid, "Mã không hợp lệ hoặc đã hết hạn.");
        }

        if (user.EmailVerifiedAt is not null)
        {
            return;
        }

        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = user.EmailVerifiedAt.Value;
        await _userRepository.UpdateAsync(user, ct);

        await _auditLogService.LogAsync(AuditEvents.EmailVerified, user.Id, ct: ct);
    }

    internal static BusinessRuleException Failure(OtpVerifyResult result) => result switch
    {
        OtpVerifyResult.TooManyAttempts => new BusinessRuleException(
            AuthErrorCodes.OtpTooManyAttempts, "Bạn đã nhập sai quá nhiều lần. Vui lòng xin mã mới."),
        _ => new BusinessRuleException(
            AuthErrorCodes.OtpInvalid, "Mã không hợp lệ hoặc đã hết hạn."),
    };
}
