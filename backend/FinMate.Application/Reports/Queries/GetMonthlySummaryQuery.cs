namespace FinMate.Application.Reports.Queries;

/// <summary>Year/Month null = tháng hiện tại (giờ VN).</summary>
public record GetMonthlySummaryQuery(Guid UserId, int? Year, int? Month);
