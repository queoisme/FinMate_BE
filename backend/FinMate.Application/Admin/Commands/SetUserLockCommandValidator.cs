using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class SetUserLockCommandValidator : AbstractValidator<SetUserLockCommand>
{
    public SetUserLockCommandValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Lý do tối đa 500 ký tự.");
    }
}
