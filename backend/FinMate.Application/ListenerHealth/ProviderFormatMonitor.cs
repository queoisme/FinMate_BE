using System.Globalization;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.ListenerHealth;

public enum FormatSignal
{
    /// <summary>BR-LH-10: tỉ lệ <c>extraction_failed</c> + <c>uncertain</c> tăng vọt.</summary>
    ErrorRate,

    /// <summary>S1: tỉ lệ <c>financial</c> tụt mạnh (giao dịch bị xếp <c>non_financial</c> sau khi mẫu tin đổi).</summary>
    FinancialShare,
}

/// <param name="State">Unhealthy khi có ít nhất một tín hiệu; Unknown khi 24 giờ qua chưa đủ mẫu (không mở, không đóng sự cố).</param>
public sealed record FormatVerdict(MonitoredServiceState State, IReadOnlyList<FormatSignal> Signals, string Detail);

/// <summary>BR-LH-10, S1, D14: hàm thuần quyết định một provider có dấu hiệu đổi mẫu tin không. Chi tiết chỉ có số đếm.</summary>
public static class ProviderFormatEvaluator
{
    public static FormatVerdict Evaluate(ProviderFormatCounts c, ListenerHealthSettings s)
    {
        var detail = Detail(c);
        if (c.RecentTotal < s.FormatDriftMinSamples)
        {
            return new FormatVerdict(MonitoredServiceState.Unknown, [], detail);
        }

        var signals = new List<FormatSignal>();
        var recentRate = (double)c.RecentFailed / c.RecentTotal;
        var baselineRate = c.BaselineTotal >= s.FormatDriftMinSamples ? (double)c.BaselineFailed / c.BaselineTotal : 0;
        var comparedRate = Math.Max(baselineRate, s.FormatDriftBaselineFloorPercent / 100.0);
        if (recentRate * 100 >= s.FormatDriftMinRatePercent && recentRate >= s.FormatDriftRatio * comparedRate)
        {
            signals.Add(FormatSignal.ErrorRate);
        }

        if (c.BaselineTotal >= s.FormatDriftMinSamples && c.BaselineFinancial > 0)
        {
            var baselineShare = (double)c.BaselineFinancial / c.BaselineTotal;
            var recentShare = (double)c.RecentFinancial / c.RecentTotal;
            if (recentShare < s.FormatFinancialShareDropRatio * baselineShare)
            {
                signals.Add(FormatSignal.FinancialShare);
            }
        }

        return new FormatVerdict(signals.Count > 0 ? MonitoredServiceState.Unhealthy : MonitoredServiceState.Healthy, signals, detail);
    }

    public static string SignalText(FormatSignal signal) => signal == FormatSignal.ErrorRate ? "error_rate" : "financial_share";

    private static string Detail(ProviderFormatCounts c)
        => $"24h: lỗi {c.RecentFailed}/{c.RecentTotal} ({Pct(c.RecentFailed, c.RecentTotal)}), giao dịch {c.RecentFinancial}/{c.RecentTotal} ({Pct(c.RecentFinancial, c.RecentTotal)}); "
           + $"7 ngày trước: lỗi {c.BaselineFailed}/{c.BaselineTotal} ({Pct(c.BaselineFailed, c.BaselineTotal)}), giao dịch {c.BaselineFinancial}/{c.BaselineTotal} ({Pct(c.BaselineFinancial, c.BaselineTotal)})";

    public static string Pct(int part, int total) => total == 0 ? "–" : (100.0 * part / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}

public interface IProviderFormatMonitor
{
    Task RunAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Mỗi giờ (D16): với từng provider, cập nhật <c>ops_service_status</c> khoá <c>format:{provider_key}</c> và mở / tự đóng sự cố mức Normal (D15).
/// Một sự cố mỗi provider cho cả hai tín hiệu (S1); đóng khi đủ <c>formatDriftRecoveryRuns</c> lượt liên tiếp bình thường. Thiếu mẫu thì giữ nguyên.
/// </summary>
public sealed class ProviderFormatMonitor : IProviderFormatMonitor
{
    public const string StatusPrefix = "format:";
    public const string FingerprintPrefix = "notification_format:";

    private readonly IProviderFormatRepository _counts;
    private readonly IMonitoringRepository _monitoring;
    private readonly IIncidentService _incidents;
    private readonly ISystemConfigProvider _config;

    public ProviderFormatMonitor(IProviderFormatRepository counts, IMonitoringRepository monitoring, IIncidentService incidents, ISystemConfigProvider config)
    {
        _counts = counts;
        _monitoring = monitoring;
        _incidents = incidents;
        _config = config;
    }

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.ListenerHealth;
        foreach (var counts in await _counts.CountAsync(now, ct))
        {
            var verdict = ProviderFormatEvaluator.Evaluate(counts, settings);
            var key = StatusPrefix + counts.ProviderKey;
            var status = await _monitoring.GetStatusAsync(key, ct);
            if (status is null)
            {
                status = new OpsServiceStatus { Service = key };
                _monitoring.AddStatus(status);
            }

            status.LastCheckedAt = now;
            status.Detail = verdict.Signals.Count == 0 ? null : string.Join(",", verdict.Signals.Select(ProviderFormatEvaluator.SignalText));
            switch (verdict.State)
            {
                case MonitoredServiceState.Unhealthy:
                    status.State = MonitoredServiceState.Unhealthy;
                    status.ConsecutiveSuccesses = 0;
                    status.ConsecutiveFailures++;
                    break;
                case MonitoredServiceState.Healthy:
                    status.State = MonitoredServiceState.Healthy;
                    status.ConsecutiveFailures = 0;
                    status.ConsecutiveSuccesses++;
                    status.LastHealthyAt = now;
                    break;
                default:
                    status.State = MonitoredServiceState.Unknown;   // giữ nguyên bộ đếm và sự cố đang mở
                    break;
            }

            await _monitoring.SaveChangesAsync(ct);

            var fingerprint = FingerprintPrefix + counts.ProviderKey;
            if (verdict.State == MonitoredServiceState.Unhealthy)
            {
                await _incidents.RaiseAsync(
                    new IncidentSignal(
                        fingerprint, IncidentSource.NotificationFormat, counts.ProviderKey, IncidentSeverity.Normal,
                        $"Có thể {counts.DisplayName} đã đổi mẫu thông báo",
                        $"Tín hiệu: {string.Join(", ", verdict.Signals.Select(ProviderFormatEvaluator.SignalText))}. {verdict.Detail}"),
                    occurrences: 1, now, ct);
            }
            else if (verdict.State == MonitoredServiceState.Healthy && status.ConsecutiveSuccesses >= settings.FormatDriftRecoveryRuns)
            {
                await _incidents.ResolveAsync(fingerprint, now, ct);
            }
        }
    }
}
