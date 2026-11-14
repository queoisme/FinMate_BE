using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.ListenerHealth;

/// <summary>Khoá cache độ phủ gắn vào dự báo (D10/S7) và cửa sổ của nó.</summary>
public static class DataCoverageCache
{
    public const int MinWindowDays = 7;
    public const int MaxWindowDays = 90;
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>S7: cửa sổ độ phủ khi gắn vào dự báo = <c>forecast.historyWindowDays</c> kẹp về 7–90.</summary>
    public static int ForecastWindowDays(ForecastSettings settings) => Math.Clamp(settings.HistoryWindowDays, MinWindowDays, MaxWindowDays);

    public static string Key(Guid userId, int windowDays) => $"data-coverage:{userId:N}:{windowDays}";
}
