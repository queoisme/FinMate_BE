using FluentValidation;
using FinMate.Application.Common;

namespace FinMate.Application.SavingGoals.Commands;

public class CreateSavingGoalCommandValidator : AbstractValidator<CreateSavingGoalCommand>
{
    public CreateSavingGoalCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên mục tiêu không được để trống.");
        RuleFor(x => x.Name).MaximumLength(200).WithMessage("Tên mục tiêu tối đa 200 ký tự.");
        RuleFor(x => x.TargetCents).GreaterThan(0).WithMessage("Số tiền mục tiêu phải lớn hơn 0.");
        RuleFor(x => x.TargetCents).LessThanOrEqualTo(MoneyLimits.MaxAmountCents).WithMessage(MoneyLimits.TooLargeMessage);
        RuleFor(x => x.Icon).MaximumLength(40).WithMessage("Tên biểu tượng tối đa 40 ký tự.");
        RuleFor(x => x.InitialSavedCents)
            .Must((c, v) => v >= 0 && v < c.TargetCents)
            .WithMessage("Số tiền đã có phải từ 0 và nhỏ hơn số tiền mục tiêu.");
        RuleFor(x => x.Deadline)
            .Must(d => d is null || d > DateTimeOffset.UtcNow)
            .WithMessage("Hạn hoàn thành phải ở tương lai.");
    }
}
