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
}
