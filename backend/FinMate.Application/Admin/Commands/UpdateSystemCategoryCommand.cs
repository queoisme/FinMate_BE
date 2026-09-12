namespace FinMate.Application.Admin.Commands;

/// <param name="Slug">Chỉ nhận để TỪ CHỐI khi khác giá trị hiện tại — xem handler.</param>
public record UpdateSystemCategoryCommand(
    Guid AdminId,
    Guid CategoryId,
    string? Name,
    string? Slug,
    string? IconName,
    string? IpAddress);
