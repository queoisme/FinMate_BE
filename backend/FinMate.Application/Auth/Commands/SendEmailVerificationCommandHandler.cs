using FinMate.Application.Auth.Otp;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class SendEmailVerificationCommandHandler : ISendEmailVerificationCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;

    public SendEmailVerificationCommandHandler(
        IUserRepository userRepository, IOtpService otpService, IEmailSender emailSender)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _emailSender = emailSender;
    }

    public async Task HandleAsync(SendEmailVerificationCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(command.Email, ct);

        // Email không tồn tại, hoặc đã xác minh rồi: im lặng kết thúc như thể đã gửi. Phân biệt
        // ra là biến endpoint này thành máy dò tài khoản.
        if (user is null || user.EmailVerifiedAt is not null)
        {
            return;
        }

        var issued = await _otpService.IssueAsync(command.Email, OtpPurpose.VerifyEmail, ct);
        if (!issued.Issued)
        {
            // Đang trong thời gian chờ. Vẫn không báo ra ngoài — biết được là biết email có thật.
            throw new BusinessRuleException(
                AuthErrorCodes.OtpRequestedTooSoon, "Vui lòng đợi một phút trước khi xin mã mới.");
        }

        var (subject, body) = OtpMessages.VerifyEmail(issued.Code!);
        await _emailSender.SendAsync(command.Email, subject, body, ct);
    }
}
