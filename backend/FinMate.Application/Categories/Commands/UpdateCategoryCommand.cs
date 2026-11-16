namespace FinMate.Application.Categories.Commands;

/// <summary>
/// Sửa danh mục riêng. <c>Name</c> và <c>IconName</c> thay hẳn như trước (IconName null = bỏ icon). Các trường mới: <c>null</c> = GIỮ NGUYÊN, để app cũ chỉ gửi tên và icon
/// không xóa mất màu hay nhóm. Muốn bỏ nhóm cha gửi <see cref="Guid.Empty"/>; muốn bỏ hạn mức gợi ý gửi <c>0</c>.
/// </summary>
public record UpdateCategoryCommand(
    Guid UserId,
    Guid CategoryId,
    string Name,
    string? IconName,
    string? Color = null,
    string? Kind = null,
    Guid? ParentCategoryId = null,
    long? SuggestedBudgetCents = null,
    bool? IsFavorite = null);
