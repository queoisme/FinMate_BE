using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Common.Utils;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Categories.Commands;

public class CreateCategoryCommandHandler : ICreateCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IValidator<CreateCategoryCommand> _validator;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, IValidator<CreateCategoryCommand> validator)
    {
        _categoryRepository = categoryRepository;
        _validator = validator;
    }

    public async Task<CategoryDto> HandleAsync(CreateCategoryCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var slug = Slugify.ToSlug(command.Name);
        if (await _categoryRepository.ExistsBySlugAsync(command.UserId, slug, ct))
        {
            throw new ConflictException(
                CategoryErrorCodes.SlugDuplicate,
                "Bạn đã có danh mục khác trùng tên.");
        }

        var now = DateTimeOffset.UtcNow;
        var category = new Category
        {
            UserId = command.UserId,
            Name = command.Name.Trim(),
            Slug = slug,
            IconName = command.IconName,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _categoryRepository.AddAsync(category, ct);

        return new CategoryDto(category.Id, category.Name, category.Slug, category.IconName, category.IsSystem, category.CreatedAt);
    }
}
