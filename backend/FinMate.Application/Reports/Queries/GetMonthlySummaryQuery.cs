namespace FinMate.Application.Reports.Queries;

/// <summary>
/// Year/Month null = tháng hiện tại (giờ VN). <paramref name="AccountId"/> null = tổng mọi ví
/// (đường mặc định, vẫn qua cache <c>daily_summaries</c>); có giá trị = chỉ ví đó, luôn tính live
/// vì <c>daily_summaries</c> không có chiều theo ví (Issue D, 2026-11-13).
/// </summary>
public record GetMonthlySummaryQuery(Guid UserId, int? Year, int? Month, Guid? AccountId = null);
