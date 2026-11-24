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

    /// <summary>Quỹ nhóm vừa chuyển sang âm (Phase 85, BR-FUND-16).</summary>
    GroupFundNegative,

    /// <summary>Người quản lý đã xoá dòng quỹ của bạn (Phase 85, S3).</summary>
    GroupFundRemoved,

    /// <summary>Nhắc đúng ngày người dùng đã hẹn mua (từ đề xuất "hoãn mua tới ngày" của agent). Không mang số tiền.</summary>
    PurchaseReminder,

    /// <summary>Bản tin tổng kết tuần (thứ Hai 08:00 giờ Việt Nam): ngân sách có nguy cơ vượt và tiến độ mục tiêu gần hạn nhất. Không mang số tiền.</summary>
    WeeklyDigest,

    /// <summary>Chủ nợ nhắc bạn trả nợ trong nhóm. Không mang số tiền.</summary>
    GroupDebtReminder,

    /// <summary>Có khoản chờ chủ nhóm duyệt. Không mang số tiền.</summary>
    GroupApprovalRequested,

    /// <summary>Chủ nhóm đã duyệt hoặc từ chối khoản bạn gửi. Không mang số tiền.</summary>
    GroupApprovalDecided,

    /// <summary>Giá thị trường mà mục tiêu tiết kiệm đang theo (ví dụ vàng) tăng làm ngày đạt mục tiêu lùi xa.</summary>
    GoalMarketShift,
}
