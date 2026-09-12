using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateSystemCategoryCommandValidator : AbstractValidator<CreateSystemCategoryCommand>
{
    public CreateSystemCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(100).WithMessage("Tên danh mục tối đa 100 ký tự.");

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("Slug không được để trống.")
            .MaximumLength(100).WithMessage("Slug tối đa 100 ký tự.")
            .Matches("^[a-z0-9_]+$").WithMessage("Slug chỉ gồm chữ thường, số và dấu gạch dưới.");

        RuleFor(x => x.IconName)
            .MaximumLength(50).WithMessage("Tên icon tối đa 50 ký tự.");
    }
}
