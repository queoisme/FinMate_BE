using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateProviderConfigCommandValidator : AbstractValidator<UpdateProviderConfigCommand>
{
    public UpdateProviderConfigCommandValidator()
    {
        RuleFor(x => x.DisplayName)
            .MaximumLength(100).WithMessage("Tên hiển thị tối đa 100 ký tự.")
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống.")
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.PackageName)
            .NotEmpty().WithMessage("package_name không được để trống.")
            .MaximumLength(200).WithMessage("package_name tối đa 200 ký tự.")
            .When(x => x.PackageName is not null);

        RuleFor(x => x.AccountType)
            .IsInEnum().WithMessage("Loại tài khoản không hợp lệ.")
            .When(x => x.AccountType is not null);
    }
}
