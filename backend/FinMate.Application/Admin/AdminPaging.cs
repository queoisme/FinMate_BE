namespace FinMate.Application.Admin;

/// <summary>
/// Giới hạn kích thước trang cho mọi danh sách admin.
///
/// Kẹp ở tầng handler chứ không phải ở controller: controller nhận số từ query string, nhưng
/// handler là thứ duy nhất mọi đường gọi đều đi qua — kẹp ở đây thì một endpoint mới thêm sau
/// này không thể vô tình mở cửa cho <c>?limit=1000000</c>.
/// </summary>
public static class AdminPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static int Clamp(int limit)
        => limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
}
