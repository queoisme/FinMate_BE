using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    public UpdateUserProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Tên hiển thị không được để trống.")
            .MaximumLength(200);

        // Trần 100 tỷ/tháng: không phải để chặn người giàu, mà để một con số gõ nhầm
        // (thừa vài số 0) không làm mọi dự báo và kiểm tra khả thi trở nên vô nghĩa.
        RuleFor(x => x.MonthlyIncomeCents!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Thu nhập không được âm.")
            .LessThanOrEqualTo(100_000_000_000).WithMessage("Thu nhập vượt quá giới hạn hợp lệ.")
            .When(x => x.MonthlyIncomeCents is not null);
    }
}
