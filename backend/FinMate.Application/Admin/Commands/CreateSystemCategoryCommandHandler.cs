using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateSystemCategoryCommandHandler : ICreateSystemCategoryCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<CreateSystemCategoryCommand> _validator;

    public CreateSystemCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IAuditLogService auditLogService,
        IValidator<CreateSystemCategoryCommand> validator)
    {
        _categoryRepository = categoryRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminCategoryDto> HandleAsync(
        CreateSystemCategoryCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var slug = command.Slug.Trim().ToLowerInvariant();

        // userId null = phạm vi category hệ thống; trùng slug trong phạm vi này bị chặn bởi
        // partial unique index uq_categories_system_slug, kiểm ở đây để trả 409 thay vì lỗi DB thô.
        if (await _categoryRepository.ExistsBySlugAsync(null, slug, ct))
        {
            throw new ConflictException(
                AdminErrorCodes.SystemCategorySlugDuplicate,
                $"Đã có danh mục hệ thống với slug '{slug}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var category = new Category
        {
            UserId = null,
            Name = command.Name.Trim(),
            Slug = slug,
            IconName = command.IconName?.Trim(),
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _categoryRepository.AddAsync(category, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminCategoryCreated,
            command.AdminId,
            command.IpAddress,
            new { category.Id, category.Slug, category.Name },
            ct);

        return AdminMapper.ToDto(category);
    }
}
