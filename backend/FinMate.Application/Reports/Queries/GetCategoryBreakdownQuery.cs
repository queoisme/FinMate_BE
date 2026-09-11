namespace FinMate.Application.Reports.Queries;

public record GetCategoryBreakdownQuery(Guid UserId, int? Year, int? Month);
