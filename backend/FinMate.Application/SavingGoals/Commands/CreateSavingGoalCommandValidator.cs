using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class CreateSavingGoalCommandValidator : AbstractValidator<CreateSavingGoalCommand>
{
    public CreateSavingGoalCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Tên mục tiêu tối đa 200 ký tự.");
        RuleFor(x => x.TargetCents).GreaterThan(0).WithMessage("Số tiền mục tiêu phải lớn hơn 0.");
        RuleFor(x => x.Deadline)
            .Must(d => d is null || d > DateTimeOffset.UtcNow)
            .WithMessage("Hạn hoàn thành phải ở tương lai.");
    }
}
