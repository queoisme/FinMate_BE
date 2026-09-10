namespace FinMate.Application.Categories.Commands;

public record DeleteCategoryCommand(Guid UserId, Guid CategoryId);
