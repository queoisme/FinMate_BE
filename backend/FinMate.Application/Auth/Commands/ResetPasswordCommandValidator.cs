using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();

        // Cùng luật với RegisterCommandValidator: đặt lại mật khẩu mà nới lỏng hơn lúc đăng ký
        // thì luật mạnh ở kia thành vô nghĩa — ai cũng đi đường vòng qua đây.
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MinimumLength(8).WithMessage("Mật khẩu phải có ít nhất 8 ký tự.")
            .Matches("[A-Z]").WithMessage("Mật khẩu phải có ít nhất 1 chữ hoa.")
            .Matches("[a-z]").WithMessage("Mật khẩu phải có ít nhất 1 chữ thường.")
            .Matches("[0-9]").WithMessage("Mật khẩu phải có ít nhất 1 chữ số.");
    }
}
