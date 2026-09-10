namespace FinMate.Application.Categories.Commands;

public record CreateCategoryCommand(Guid UserId, string Name, string? IconName);
