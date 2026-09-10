namespace FinMate.Application.Categories.Commands;

public record UpdateCategoryCommand(Guid UserId, Guid CategoryId, string Name, string? IconName);
