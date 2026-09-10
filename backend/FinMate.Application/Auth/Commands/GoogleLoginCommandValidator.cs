using FluentValidation;

namespace FinMate.Application.Auth.Commands;

public class GoogleLoginCommandValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("idToken không được để trống.");
    }
}
