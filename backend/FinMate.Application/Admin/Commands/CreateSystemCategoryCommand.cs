namespace FinMate.Application.Admin.Commands;

/// <param name="Slug">
/// Nhận tường minh từ admin chứ KHÔNG tự sinh từ <paramref name="Name"/> như category của người
/// dùng: slug hệ thống là taxonomy mà AI Service trả về trong <c>category_slug</c>, nên nó phải
/// do người quyết định và khớp với phía AI, không phải là hệ quả của cách gõ tên tiếng Việt.
/// </param>
public record CreateSystemCategoryCommand(
    Guid AdminId,
    string Name,
    string Slug,
    string? IconName,
    string? IpAddress);
