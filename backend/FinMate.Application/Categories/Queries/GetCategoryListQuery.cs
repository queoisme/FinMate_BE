namespace FinMate.Application.Categories.Queries;

public record GetCategoryListQuery(Guid UserId, bool IncludeHidden = false);
