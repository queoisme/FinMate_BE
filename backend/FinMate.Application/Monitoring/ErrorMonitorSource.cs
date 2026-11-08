using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Monitoring;

/// <summary>Đếm lỗi ứng dụng theo <c>fingerprint</c> để mở sự cố, và tự đóng khi lỗi không còn lặp lại. Một lỗi lẻ không làm phiền ai, nhưng vẫn xem được trong danh sách lỗi.</summary>
public sealed class ErrorMonitorSource : IMonitorSource
{
    /// <summary>Vượt ngưỡng từng này lần thì coi là lỗi hàng loạt.</summary>
    private const int CriticalMultiplier = 5;

    private readonly IMonitoringRepository _repository;
    private readonly IIncidentService _incidents;

    public ErrorMonitorSource(IMonitoringRepository repository, IIncidentService incidents)
    {
        _repository = repository;
        _incidents = incidents;
    }

    public async Task RunAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        var groups = await _repository.GroupRecentErrorsAsync(IncidentSource.AppError, now.AddMinutes(-settings.ErrorWindowMinutes), ct);
        foreach (var group in groups.Where(g => g.Count >= settings.ErrorCountThreshold))
        {
            // Cửa sổ chồng lên nhau giữa các lượt, nên chỉ cộng phần mới để số lần xuất hiện không bị đếm trùng.
            var existing = await _repository.GetActiveByFingerprintAsync(group.Fingerprint, ct);
            var occurrences = existing is null ? group.Count : await _repository.CountErrorsSinceAsync(group.Fingerprint, existing.LastSeenAt, ct);
            var severity = group.Count >= settings.ErrorCountThreshold * CriticalMultiplier ? IncidentSeverity.Critical : IncidentSeverity.High;

            await _incidents.RaiseAsync(
                new IncidentSignal(group.Fingerprint, IncidentSource.AppError, group.Service, severity, "Lỗi hệ thống lặp lại", Detail(group)),
                occurrences,
                now,
                ct);
        }

        var quietSince = now.AddMinutes(-settings.ErrorQuietMinutes);
        foreach (var fingerprint in await _repository.ListQuietIncidentFingerprintsAsync(IncidentSource.AppError, quietSince, ct))
        {
            await _incidents.ResolveAsync(fingerprint, now, ct);
        }
    }

    private static string Detail(ErrorGroup group)
    {
        var parts = new[] { group.ExceptionType, group.SourceContext, group.RequestPath }.Where(p => !string.IsNullOrEmpty(p));
        var detail = string.Join(" | ", parts);
        return detail.Length <= 1000 ? detail : detail[..1000];
    }
}
