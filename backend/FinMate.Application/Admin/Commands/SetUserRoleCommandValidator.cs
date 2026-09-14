using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class SetUserRoleCommandValidator : AbstractValidator<SetUserRoleCommand>
{
    public SetUserRoleCommandValidator()
    {
        RuleFor(x => x.Role).IsInEnum();

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Lý do tối đa 500 ký tự.");
    }
}
