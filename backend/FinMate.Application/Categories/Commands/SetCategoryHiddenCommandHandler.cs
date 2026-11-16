using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Categories.Commands;

public record SetCategoryHiddenCommand(Guid UserId, Guid CategoryId, bool Hidden);

public interface ISetCategoryHiddenCommandHandler
{
    Task HandleAsync(SetCategoryHiddenCommand command, CancellationToken ct = default);
}

/// <summary>Ẩn/hiện danh mục hệ thống cho riêng một người dùng. Chỉ danh mục hệ thống đang dùng; "Khác" là chỗ rơi cuối của phân loại nên không ẩn được.</summary>
public class SetCategoryHiddenCommandHandler(ICategoryRepository categories) : ISetCategoryHiddenCommandHandler
{
    public const string FallbackSlug = "other";

    public async Task HandleAsync(SetCategoryHiddenCommand command, CancellationToken ct = default)
    {
        var category = await categories.GetByIdAsync(command.CategoryId, ct);
        if (category is null || !category.IsSystem || category.UserId is not null || !category.IsActive)
        {
            throw new NotFoundException("Category", command.CategoryId);
        }

        if (command.Hidden && category.Slug == FallbackSlug)
        {
            throw new BusinessRuleException(CategoryErrorCodes.CannotHide, "Danh mục \"Khác\" không thể ẩn.");
        }

        await categories.SetHiddenAsync(command.UserId, category.Id, command.Hidden, ct);
    }
}

public static class CategoryVisibility
{
    /// <summary>Danh mục hệ thống theo slug AI trả về; null nếu người dùng đã ẩn nó: giao dịch mới không rơi vào danh mục họ đã bỏ (người dùng tự chọn).</summary>
    public static async Task<FinMate.Domain.Entities.Category?> GetVisibleSystemBySlugAsync(
        this ICategoryRepository categories, Guid userId, string slug, CancellationToken ct = default)
    {
        var category = await categories.GetSystemBySlugAsync(slug, ct);
        return category is not null && await categories.IsHiddenAsync(userId, category.Id, ct) ? null : category;
    }
}
