using FluentValidation;
using FinMate.Application.Common;

namespace FinMate.Application.SavingGoals.Commands;

public class ContributeToGoalCommandValidator : AbstractValidator<ContributeToGoalCommand>
{
    public ContributeToGoalCommandValidator()
    {
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền đóng góp phải lớn hơn 0.");
        RuleFor(x => x.AmountCents).LessThanOrEqualTo(MoneyLimits.MaxAmountCents).WithMessage(MoneyLimits.TooLargeMessage);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
