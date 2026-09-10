using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Categories.Commands;

public class UpdateCategoryCommandHandler : IUpdateCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IValidator<UpdateCategoryCommand> _validator;

    public UpdateCategoryCommandHandler(ICategoryRepository categoryRepository, IValidator<UpdateCategoryCommand> validator)
    {
        _categoryRepository = categoryRepository;
        _validator = validator;
    }

    public async Task<CategoryDto> HandleAsync(UpdateCategoryCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // GetOwnedByUserAsync chỉ trả category do chính user tạo — system category (UserId=null)
        // và category của user khác đều trả null ở đây, ném NotFound thay vì Forbidden để không
        // lộ sự tồn tại của resource không thuộc về mình (đúng AGENTS.md §6.2 cross-user isolation).
        var category = await _categoryRepository.GetOwnedByUserAsync(command.CategoryId, command.UserId, ct)
            ?? throw new NotFoundException("Category", command.CategoryId);

        category.Name = command.Name.Trim();
        category.IconName = command.IconName;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await _categoryRepository.UpdateAsync(category, ct);

        return new CategoryDto(category.Id, category.Name, category.Slug, category.IconName, category.IsSystem, category.CreatedAt);
    }
}
