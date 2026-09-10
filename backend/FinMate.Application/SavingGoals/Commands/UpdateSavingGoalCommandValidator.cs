using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class UpdateSavingGoalCommandValidator : AbstractValidator<UpdateSavingGoalCommand>
{
    public UpdateSavingGoalCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Tên mục tiêu tối đa 200 ký tự.");
        RuleFor(x => x.TargetCents).GreaterThan(0).WithMessage("Số tiền mục tiêu phải lớn hơn 0.");
    }
}
