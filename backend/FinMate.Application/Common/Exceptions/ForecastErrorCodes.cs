namespace FinMate.Application.Common.Exceptions;

public static class ForecastErrorCodes
{
    /// <summary>Ngày kết thúc dự báo trước hôm nay hoặc xa hơn mức tối đa cấu hình (<c>forecast.maxHorizonDays</c>).</summary>
    public const string HorizonInvalid = "FORECAST_HORIZON_INVALID";

    /// <summary>Kịch bản có quá nhiều khoản giả định hoặc khoản nằm ngoài kỳ dự báo.</summary>
    public const string ScenarioInvalid = "FORECAST_SCENARIO_INVALID";

    /// <summary>Yêu cầu đánh giá khoản mua sai: giá không hợp lệ, ngày mua ngoài kỳ, mốc thời gian ngoài mức cho phép.</summary>
    public const string PurchaseInvalid = "PURCHASE_CHECK_INVALID";

    /// <summary>Đã có khoản định kỳ đang theo dõi cho cùng người nhận và cùng chiều.</summary>
    public const string RecurringDuplicate = "RECURRING_ITEM_DUPLICATE";

    /// <summary>Chuyển trạng thái không hợp lệ (vd xác nhận một khoản không ở trạng thái đề xuất).</summary>
    public const string RecurringState = "RECURRING_ITEM_STATE";

    /// <summary>Danh mục hoặc ví gắn với khoản định kỳ không thuộc về người dùng.</summary>
    public const string RecurringReferenceInvalid = "RECURRING_ITEM_REFERENCE_INVALID";
}
