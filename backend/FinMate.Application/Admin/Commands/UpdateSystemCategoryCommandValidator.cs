using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateSystemCategoryCommandValidator : AbstractValidator<UpdateSystemCategoryCommand>
{
    public UpdateSystemCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(100).WithMessage("Tên danh mục tối đa 100 ký tự.")
            .When(x => x.Name is not null);

        RuleFor(x => x.IconName)
            .MaximumLength(50).WithMessage("Tên icon tối đa 50 ký tự.")
            .When(x => x.IconName is not null);
    }
}
