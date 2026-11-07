namespace FinMate.Application.Admin.Queries;

/// <param name="Search">Khớp tên hoặc mã (slug), không phân biệt hoa thường và dấu.</param>
public record GetSystemCategoryListQuery(bool IncludeInactive, string? Search = null, int Offset = 0, int Limit = 0);
