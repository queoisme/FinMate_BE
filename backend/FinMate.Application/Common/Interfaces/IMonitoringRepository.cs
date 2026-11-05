using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record IncidentFilter(IncidentStatus? Status, IncidentSeverity? Severity, bool ActiveOnly, int Offset, int Limit);

public sealed record AlertRecipient(Guid UserId, string Email);

public interface IMonitoringRepository
{
    Task<List<OpsServiceStatus>> ListStatusesAsync(CancellationToken ct = default);
    Task<OpsServiceStatus?> GetStatusAsync(string service, CancellationToken ct = default);
    void AddStatus(OpsServiceStatus status);

    Task<OpsIncident?> GetActiveByFingerprintAsync(string fingerprint, CancellationToken ct = default);

    /// <summary>Thêm sự cố mới. Trả false nếu tiến trình khác vừa mở cùng lỗi, nhờ unique index; khi đó người gọi đọc lại và tăng đếm.</summary>
    Task<bool> TryAddIncidentAsync(OpsIncident incident, CancellationToken ct = default);
    Task<OpsIncident?> GetIncidentAsync(Guid id, CancellationToken ct = default);
    Task<(List<OpsIncident> Items, int Total)> ListIncidentsAsync(IncidentFilter filter, CancellationToken ct = default);

    /// <summary>Sự cố đang mở ở các mức cần báo ngay. Sự cố đã có người nhận không nằm trong đây.</summary>
    Task<List<OpsIncident>> ListOpenUrgentAsync(CancellationToken ct = default);
    Task<List<OpsIncident>> ListOpenNormalAsync(CancellationToken ct = default);

    /// <summary>Sự cố đã đóng, đã từng báo, chưa báo đóng.</summary>
    Task<List<OpsIncident>> ListResolvedUnnotifiedAsync(CancellationToken ct = default);

    /// <summary>Tài khoản đang hoạt động có email. <paramref name="includeManagers"/> false thì chỉ Admin.</summary>
    Task<List<AlertRecipient>> ListAlertRecipientsAsync(bool includeManagers, CancellationToken ct = default);

    Task<int> DeleteErrorEventsBeforeAsync(DateTimeOffset before, CancellationToken ct = default);

    /// <summary>Bỏ các thay đổi đang theo dõi, để lỗi ở một nguồn không kéo theo nguồn kế tiếp.</summary>
    void ResetTracking();
    Task SaveChangesAsync(CancellationToken ct = default);
}
