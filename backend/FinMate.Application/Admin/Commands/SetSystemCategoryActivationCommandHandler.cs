using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public class SetSystemCategoryActivationCommandHandler : ISetSystemCategoryActivationCommandHandler
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IAiCategorySyncService _aiCategorySync;

    public SetSystemCategoryActivationCommandHandler(
        ICategoryRepository categoryRepository,
        IAuditLogService auditLogService,
        IAiCategorySyncService aiCategorySync)
    {
        _categoryRepository = categoryRepository;
        _auditLogService = auditLogService;
        _aiCategorySync = aiCategorySync;
    }

    public async Task<AdminCategoryDto> HandleAsync(
        SetSystemCategoryActivationCommand command, CancellationToken ct = default)
    {
        var category = await _categoryRepository.GetByIdAsync(command.CategoryId, ct);
        if (category is null || category.UserId is not null)
        {
            throw new NotFoundException("SystemCategory", command.CategoryId);
        }

        if (category.IsActive == command.IsActive)
        {
            return AdminMapper.ToDto(category);
        }

        // is_active chứ không phải deleted_at: Category có global query filter trên deleted_at,
        // nên soft delete sẽ kéo chi tiêu CŨ của danh mục này ra khỏi category-breakdown (báo
        // cáo đi qua navigation Transaction.Category). Tắt = ngừng cho chọn mới, lịch sử giữ nguyên.
        category.IsActive = command.IsActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await _categoryRepository.UpdateAsync(category, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminCategoryActivationChanged,
            command.AdminId,
            command.IpAddress,
            new { category.Id, category.Slug, category.IsActive },
            ct);

        // Tắt/bật phải sang AI Service để nó ngừng (hoặc tiếp tục) nhận danh mục này cho dữ liệu mới; thất bại thì job định kỳ kéo lại.
        await _aiCategorySync.TrySyncAsync(
            command.AdminId, $"Admin {(category.IsActive ? "bật" : "tắt")} danh mục hệ thống '{category.Slug}'", ct);

        return AdminMapper.ToDto(category);
    }
}
