namespace FinMate.Application.Forecasting;

/// <summary>Giới hạn trang cho các danh sách phân trang kiểu con trỏ trong tính năng dự báo.</summary>
public static class ForecastPaging
{
    public const int DefaultLimit = 20;

    public static int Clamp(int limit, int max) => limit <= 0 ? DefaultLimit : Math.Min(limit, max);
}
