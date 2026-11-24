namespace FinMate.Domain.Enums;

/// <summary>Loại thông báo trong hộp thư trong app (Phân hệ B, màn B2). Cột TEXT dạng snake_case.</summary>
public enum UserNotificationType
{
    BudgetThreshold,
    BalanceChange,
    AnomalyDetected,
    RecurringBillDue,
    ForecastReady,
    GoalMilestone,

    /// <summary>Push "Có giao dịch cần xác nhận" (giao dịch nháp từ thông báo ngân hàng).</summary>
    TransactionReview,

    /// <summary>Push cảnh báo dự báo không thuộc loại nào ở trên (thiếu tiền, thu nhập trễ, thị trường, nhóm...).</summary>
    ForecastAlert,

    /// <summary>Push còn lại: khoản vay, gói đăng ký, hỗ trợ, nhóm... Chỉ hiện ở tab "Tất cả".</summary>
    General,
}
