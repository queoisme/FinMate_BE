using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class ParseNaturalLanguageCommandValidator : AbstractValidator<ParseNaturalLanguageCommand>
{
    public ParseNaturalLanguageCommandValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Nội dung không được để trống.")
            .MaximumLength(500).WithMessage("Nội dung tối đa 500 ký tự.");
    }
}
