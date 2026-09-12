using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateMissionCommandValidator : AbstractValidator<UpdateMissionCommand>
{
    public UpdateMissionCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tiêu đề không được để trống.")
            .MaximumLength(200).WithMessage("Tiêu đề tối đa 200 ký tự.")
            .When(x => x.Title is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Mô tả tối đa 500 ký tự.")
            .When(x => x.Description is not null);

        RuleFor(x => x.ConditionTarget!.Value)
            .GreaterThan(0).WithMessage("Mục tiêu phải lớn hơn 0.")
            .LessThanOrEqualTo(1000).WithMessage("Mục tiêu tối đa 1000.")
            .When(x => x.ConditionTarget is not null);

        RuleFor(x => x.ExpReward!.Value)
            .GreaterThan(0).WithMessage("Điểm thưởng phải lớn hơn 0.")
            .LessThanOrEqualTo(10000).WithMessage("Điểm thưởng tối đa 10000.")
            .When(x => x.ExpReward is not null);
    }
}
