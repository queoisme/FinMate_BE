namespace FinMate.Domain.Enums;

/// <summary>Loại cảnh báo dự báo (Flow 6, đợt 2). Cột TEXT dạng snake_case.</summary>
public enum ForecastAlertKind
{
    LowBalance,
    BudgetRisk,
    BillDue,
    BillStatusCheck,
    IncomeLate,
    PriceChange,

    /// <summary>Khoản chi bất thường mức cao (đợt 3).</summary>
    Anomaly,

    /// <summary>Ngân sách nhóm chạm ngưỡng; gửi riêng từng thành viên.</summary>
    GroupBudget,

    /// <summary>Mục tiêu nhóm hoàn thành hoặc quá hạn; gửi riêng từng thành viên.</summary>
    GroupGoal,

    /// <summary>Giá tăng ảnh hưởng tới danh mục đang căng.</summary>
    MarketChange,

    /// <summary>Dịch vụ đọc thông báo có vẻ ngừng: mất quyền hoặc ví im lặng bất thường (Phase 84). Không mang số tiền.</summary>
    ListenerHealth,
}
