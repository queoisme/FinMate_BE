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
