using FluentValidation;

namespace FinMate.Application.Notifications.Commands;

public class AnalyzeNotificationCommandValidator : AbstractValidator<AnalyzeNotificationCommand>
{
    public AnalyzeNotificationCommandValidator()
    {
        RuleFor(x => x.PackageName).NotEmpty().WithMessage("Package name không được để trống.");
        RuleFor(x => x.NotificationBody).NotEmpty().WithMessage("Nội dung notification không được để trống.");
    }
}
