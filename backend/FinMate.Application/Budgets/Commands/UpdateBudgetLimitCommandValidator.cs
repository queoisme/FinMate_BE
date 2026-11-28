using FluentValidation;
using FinMate.Application.Common;

namespace FinMate.Application.Budgets.Commands;

public class UpdateBudgetLimitCommandValidator : AbstractValidator<UpdateBudgetLimitCommand>
{
    public UpdateBudgetLimitCommandValidator()
    {
        RuleFor(x => x.LimitCents).GreaterThan(0).WithMessage("Hạn mức phải lớn hơn 0.");
        RuleFor(x => x.LimitCents).LessThanOrEqualTo(MoneyLimits.MaxAmountCents).WithMessage(MoneyLimits.TooLargeMessage);
    }
}
