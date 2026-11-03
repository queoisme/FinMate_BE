using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Persistence.Configurations;

/// <summary>Enum ↔ cột TEXT dạng snake_case (CONVENTIONS: enum là TEXT + CHECK, không dùng PostgreSQL ENUM).</summary>
internal static class ForecastExclusionReasonColumn
{
    public static string ToColumn(ForecastExclusionReason value) => value switch
    {
        ForecastExclusionReason.OneOff => "one_off",
        ForecastExclusionReason.UserMarked => "user_marked",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ForecastExclusionReason FromColumn(string value) => value switch
    {
        "one_off" => ForecastExclusionReason.OneOff,
        "user_marked" => ForecastExclusionReason.UserMarked,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}

internal static class ForecastAlertKindColumn
{
    private static readonly Dictionary<ForecastAlertKind, string> ToText = new()
    {
        [ForecastAlertKind.LowBalance] = "low_balance",
        [ForecastAlertKind.BudgetRisk] = "budget_risk",
        [ForecastAlertKind.BillDue] = "bill_due",
        [ForecastAlertKind.BillStatusCheck] = "bill_status_check",
        [ForecastAlertKind.IncomeLate] = "income_late",
        [ForecastAlertKind.PriceChange] = "price_change",
        [ForecastAlertKind.Anomaly] = "anomaly",
    };

    private static readonly Dictionary<string, ForecastAlertKind> FromText = ToText.ToDictionary(p => p.Value, p => p.Key);

    public static string ToColumn(ForecastAlertKind value) => ToText[value];

    public static ForecastAlertKind FromColumn(string value) => FromText[value];
}

internal static class AnomalyKindColumn
{
    private static readonly Dictionary<AnomalyKind, string> ToText = new()
    {
        [AnomalyKind.CategorySpike] = "category_spike",
        [AnomalyKind.MerchantSpike] = "merchant_spike",
        [AnomalyKind.Duplicate] = "duplicate",
    };

    private static readonly Dictionary<string, AnomalyKind> FromText = ToText.ToDictionary(p => p.Value, p => p.Key);

    public static string ToColumn(AnomalyKind value) => ToText[value];

    public static AnomalyKind FromColumn(string value) => FromText[value];
}

internal static class AnomalyResolutionColumn
{
    private static readonly Dictionary<AnomalyResolution, string> ToText = new()
    {
        [AnomalyResolution.Normal] = "normal",
        [AnomalyResolution.OneOff] = "one_off",
        [AnomalyResolution.WrongEntry] = "wrong_entry",
        [AnomalyResolution.Suspicious] = "suspicious",
    };

    private static readonly Dictionary<string, AnomalyResolution> FromText = ToText.ToDictionary(p => p.Value, p => p.Key);

    public static string ToColumn(AnomalyResolution value) => ToText[value];

    public static AnomalyResolution FromColumn(string value) => FromText[value];
}
