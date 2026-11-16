namespace FinMate.Application.Common.Models;

/// <param name="Cursor">Danh sách phân trang kiểu keyset (users, audit...).</param>
/// <param name="Total">Tổng số bản ghi khớp bộ lọc, cho danh sách phân trang kiểu offset. Null khi không đếm.</param>
/// <param name="Offset">Vị trí bắt đầu của trang này (offset).</param>
/// <param name="Limit">Cỡ trang thực tế sau khi kẹp.</param>
public record ApiMeta(string? Cursor = null, int? Total = null, int? Offset = null, int? Limit = null);

public record ApiResponse<T>(bool Success, T? Data, ApiError? Error = null, ApiMeta? Meta = null)
{
    public static ApiResponse<T> Ok(T data) => new(true, data);
    public static ApiResponse<T> Ok(T data, ApiMeta meta) => new(true, data, Meta: meta);
    public static ApiResponse<T> Fail(ApiError error) => new(false, default, error);
}
