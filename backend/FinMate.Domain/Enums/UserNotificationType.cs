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
}
