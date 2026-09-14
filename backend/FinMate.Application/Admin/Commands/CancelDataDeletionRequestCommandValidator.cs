using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CancelDataDeletionRequestCommandValidator
    : AbstractValidator<CancelDataDeletionRequestCommand>
{
    public CancelDataDeletionRequestCommandValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Lý do tối đa 500 ký tự.");
    }
}
