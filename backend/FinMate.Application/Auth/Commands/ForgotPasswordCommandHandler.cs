using FinMate.Application.Auth.Otp;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

/// <summary>
/// Trước Phase 17 quên mật khẩu là mất tài khoản vĩnh viễn: chỉ có <c>change-password</c> và
/// nó đòi đang đăng nhập.
/// </summary>
public class ForgotPasswordCommandHandler : IForgotPasswordCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordCommandHandler(
        IUserRepository userRepository, IOtpService otpService, IEmailSender emailSender)
    {
        _userRepository = userRepository;
        _otpService = otpService;
        _emailSender = emailSender;
    }

    public async Task HandleAsync(ForgotPasswordCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(command.Email, ct);

        // KHÔNG bao giờ để lộ email có tồn tại hay không — kể cả qua mã lỗi, kể cả qua việc
        // ném khi đang trong thời gian chờ. Endpoint này ẩn danh, nên phân biệt được là dò
        // được cả danh sách người dùng.
        //
        // Cả nhánh "đang chờ" cũng nuốt, khác với gửi lại mã xác minh: ở đó người dùng đã biết
        // email của chính mình có thật, còn ở đây thì chưa chắc.
        if (user is null)
        {
            return;
        }

        var issued = await _otpService.IssueAsync(command.Email, OtpPurpose.ResetPassword, ct);
        if (!issued.Issued)
        {
            return;
        }

        var (subject, body) = OtpMessages.ResetPassword(issued.Code!);
        await _emailSender.SendAsync(command.Email, subject, body, ct);
    }
}
