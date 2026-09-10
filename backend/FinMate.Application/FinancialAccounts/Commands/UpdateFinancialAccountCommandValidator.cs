using FluentValidation;

namespace FinMate.Application.FinancialAccounts.Commands;

public class UpdateFinancialAccountCommandValidator : AbstractValidator<UpdateFinancialAccountCommand>
{
    public UpdateFinancialAccountCommandValidator()
    {
        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("Tên tài khoản không được để trống.")
            .MaximumLength(100).WithMessage("Tên tài khoản tối đa 100 ký tự.");
    }
}
