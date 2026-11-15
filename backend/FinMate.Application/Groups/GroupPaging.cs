namespace FinMate.Application.Groups;

/// <summary>Giới hạn trang cho các danh sách phân trang kiểu con trỏ trong tính năng nhóm.</summary>
public static class GroupPaging
{
    public const int DefaultLimit = 20;

    public static int Clamp(int limit, int max) => limit <= 0 ? DefaultLimit : Math.Min(limit, max);
}
