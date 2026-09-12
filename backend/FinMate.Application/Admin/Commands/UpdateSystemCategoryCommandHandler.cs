using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateSystemCategoryCommandHandler : IUpdateSystemCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<UpdateSystemCategoryCommand> _validator;

    public UpdateSystemCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IAuditLogService auditLogService,
        IValidator<UpdateSystemCategoryCommand> validator)
    {
        _categoryRepository = categoryRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminCategoryDto> HandleAsync(
        UpdateSystemCategoryCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var category = await _categoryRepository.GetByIdAsync(command.CategoryId, ct);
        if (category is null || category.UserId is not null)
        {
            // Category của một người dùng cụ thể không thuộc phạm vi admin — trả 404 thay vì
            // 403 để không tiết lộ nó có tồn tại hay không.
            throw new NotFoundException("SystemCategory", command.CategoryId);
        }

        if (command.Slug is not null
            && !string.Equals(command.Slug.Trim(), category.Slug, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField,
                "Không đổi được slug của danh mục hệ thống: AI Service trả về chính giá trị này "
                + "trong category_slug, đổi đi thì mọi giao dịch mới sẽ mất danh mục.");
        }

        if (command.Name is not null)
        {
            category.Name = command.Name.Trim();
        }

        if (command.IconName is not null)
        {
            category.IconName = command.IconName.Trim();
        }

        category.UpdatedAt = DateTimeOffset.UtcNow;
        await _categoryRepository.UpdateAsync(category, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminCategoryUpdated,
            command.AdminId,
            command.IpAddress,
            new { category.Id, category.Slug, category.Name, category.IconName },
            ct);

        return AdminMapper.ToDto(category);
    }
}
