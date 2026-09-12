using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateMissionCommandValidator : AbstractValidator<CreateMissionCommand>
{
    public CreateMissionCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã nhiệm vụ không được để trống.")
            .MaximumLength(50).WithMessage("Mã nhiệm vụ tối đa 50 ký tự.")
            .Matches("^[a-z0-9_]+$").WithMessage("Mã nhiệm vụ chỉ gồm chữ thường, số và dấu gạch dưới.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề không được để trống.")
            .MaximumLength(200).WithMessage("Tiêu đề tối đa 200 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Mô tả tối đa 500 ký tự.");

        RuleFor(x => x.PeriodType).IsInEnum().WithMessage("Loại chu kỳ không hợp lệ.");
        RuleFor(x => x.ConditionType).IsInEnum().WithMessage("Loại điều kiện không hợp lệ.");

        // Mục tiêu 0 khiến nhiệm vụ hoàn thành ngay khi được tạo ra.
        RuleFor(x => x.ConditionTarget)
            .GreaterThan(0).WithMessage("Mục tiêu phải lớn hơn 0.")
            .LessThanOrEqualTo(1000).WithMessage("Mục tiêu tối đa 1000.");

        RuleFor(x => x.ExpReward)
            .GreaterThan(0).WithMessage("Điểm thưởng phải lớn hơn 0.")
            .LessThanOrEqualTo(10000).WithMessage("Điểm thưởng tối đa 10000.");
    }
}
