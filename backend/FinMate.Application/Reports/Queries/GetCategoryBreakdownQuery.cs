namespace FinMate.Application.Reports.Queries;

/// <param name="Type">"expense" (mặc định, giữ nguyên hợp đồng cũ) hoặc "income" — F2 tab "Thu nhập".</param>
public record GetCategoryBreakdownQuery(Guid UserId, int? Year, int? Month, string Type = "expense");
