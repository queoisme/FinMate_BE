using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

public class UpdateBudgetLimitCommandValidator : AbstractValidator<UpdateBudgetLimitCommand>
{
    public UpdateBudgetLimitCommandValidator()
    {
        RuleFor(x => x.LimitCents).GreaterThan(0).WithMessage("Hạn mức phải lớn hơn 0.");
    }
}
