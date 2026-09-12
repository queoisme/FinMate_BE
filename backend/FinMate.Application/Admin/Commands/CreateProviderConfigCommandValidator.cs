using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateProviderConfigCommandValidator : AbstractValidator<CreateProviderConfigCommand>
{
    public CreateProviderConfigCommandValidator()
    {
        // provider_key là khóa định danh ổn định mà ProviderConfigSeeder và bảng
        // provider_patterns bên AI Service cùng tra theo — giới hạn về chữ thường/gạch dưới
        // để nó không bao giờ cần escape ở đâu cả.
        RuleFor(x => x.ProviderKey)
            .NotEmpty().WithMessage("provider_key không được để trống.")
            .MaximumLength(50).WithMessage("provider_key tối đa 50 ký tự.")
            .Matches("^[a-z0-9_]+$").WithMessage("provider_key chỉ gồm chữ thường, số và dấu gạch dưới.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống.")
            .MaximumLength(100).WithMessage("Tên hiển thị tối đa 100 ký tự.");

        RuleFor(x => x.PackageName)
            .NotEmpty().WithMessage("package_name không được để trống.")
            .MaximumLength(200).WithMessage("package_name tối đa 200 ký tự.");

        RuleFor(x => x.AccountType)
            .IsInEnum().WithMessage("Loại tài khoản không hợp lệ.");
    }
}
