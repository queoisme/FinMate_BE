using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

public class CreateBudgetCommandValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetCommandValidator()
    {
        RuleFor(x => x.LimitCents).GreaterThan(0).WithMessage("Hạn mức phải lớn hơn 0.");
    }
}
