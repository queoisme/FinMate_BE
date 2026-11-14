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
}
