using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <param name="SelectedPackagesJson">Mảng JSON đã lọc theo D2.</param>
/// <param name="ReceivedAt">Giờ server nhận; ngày của dòng theo ngày là <paramref name="Day"/> (giờ VN của mốc này).</param>
public sealed record ListenerReport(
    Guid UserId,
    Guid InstallationId,
    bool PermissionGranted,
    bool ListenerConnected,
    string SelectedPackagesJson,
    string? AppVersion,
    string? Manufacturer,
    DateTimeOffset ReceivedAt,
    DateOnly Day);

public interface IListenerHealthRepository
{
    Task<bool> DeviceExistsAsync(Guid userId, Guid installationId, CancellationToken ct = default);

    Task<int> CountDevicesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Package của các ví (chưa xoá) của chính user cộng package của mọi provider đã biết: chỉ những package này được lưu (D2).</summary>
    Task<IReadOnlySet<string>> GetKnownPackagesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Ghi trạng thái mới nhất và gộp vào dòng của ngày trong MỘT câu lệnh, nên hai lần báo đồng thời không đụng unique.
    /// Dòng ngày: mất quyền ở bất kỳ lần nào thì giữ, còn trạng thái "lần cuối" theo giờ server nhận (S3).
    /// </summary>
    Task RecordReportAsync(ListenerReport report, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceListenerStatus>> GetStatusesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Các dòng theo ngày của mọi thiết bị của user từ <paramref name="fromDay"/> (gồm cả ngày đó).</summary>
    Task<IReadOnlyList<DeviceListenerDay>> GetDaysAsync(Guid userId, DateOnly fromDay, CancellationToken ct = default);

    /// <summary>
    /// BR-LH-03: với mỗi ví, các thời điểm nhận thông báo từ (thông báo cuối − <paramref name="historyDays"/>) trở đi. Thời điểm ở tương lai bị kẹp
    /// về <paramref name="now"/> trước khi tìm thông báo cuối. Ví không có thông báo nào thì không có mục.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, List<DateTimeOffset>>> GetNotificationTimesAsync(
        Guid userId, IReadOnlyCollection<Guid> accountIds, int historyDays, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>BR-LH-07: số ngày (giờ VN) có ít nhất một thông báo của mỗi ví, từ <paramref name="fromDay"/> tới hết hôm nay.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetNotificationDayCountsAsync(
        Guid userId, IReadOnlyCollection<Guid> accountIds, DateOnly fromDay, CancellationToken ct = default);
}
