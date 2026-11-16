namespace FinMate.Application.Categories.Commands;

/// <param name="Kind">null = <c>expense</c>.</param>
/// <param name="ParentCategoryId">Phải là danh mục hệ thống đang dùng.</param>
public record CreateCategoryCommand(
    Guid UserId,
    string Name,
    string? IconName,
    string? Color = null,
    string? Kind = null,
    Guid? ParentCategoryId = null,
    long? SuggestedBudgetCents = null,
    bool IsFavorite = false);
