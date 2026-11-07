namespace FinMate.Application.Admin.Queries;

/// <param name="Search">Khớp tên hiển thị, provider_key hoặc package name, không phân biệt hoa thường và dấu.</param>
public record GetProviderConfigListQuery(bool IncludeInactive, string? Search = null, int Offset = 0, int Limit = 0);
