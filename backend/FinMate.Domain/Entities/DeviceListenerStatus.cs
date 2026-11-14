namespace FinMate.Domain.Entities;

/// <summary>
/// Trạng thái mới nhất của dịch vụ đọc thông báo trên một thiết bị (Phase 84, BR-LH-01/02). Khoá là <c>(user, installation_id)</c>, không phải token FCM:
/// token xoay vòng, còn <see cref="InstallationId"/> do app sinh một lần mỗi lần cài (D1). Không chứa nội dung thông báo hay danh sách app ngoài các ví đã chọn.
/// </summary>
public class DeviceListenerStatus
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid InstallationId { get; set; }
    public bool PermissionGranted { get; set; }
    public bool ListenerConnected { get; set; }

    /// <summary>JSON <c>[{packageName, notificationsEnabled?, lastPostedAt?}]</c>, đã lọc chỉ còn package của ví user hoặc của provider đã biết (D2).</summary>
    public string SelectedPackages { get; set; } = "[]";

    public string? AppVersion { get; set; }

    /// <summary>Chữ thường; để app chọn trang hướng dẫn tự khởi động / tối ưu pin theo hãng.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Lúc chuyển sang mất quyền; null khi quyền còn. Là mốc của một đợt nhắc <c>permission_lost</c> (D17).</summary>
    public DateTimeOffset? PermissionLostSince { get; set; }

    public DateTimeOffset FirstReportedAt { get; set; }
    public DateTimeOffset LastReportedAt { get; set; }

    public User? User { get; set; }
}

/// <summary>
/// Một dòng mỗi ngày (giờ VN, theo lúc server nhận) cho mỗi thiết bị (BR-LH-02). Ngày ok khi không lần báo nào mất quyền và lần báo CUỐI của ngày
/// còn kết nối (S3): mất kết nối giữa ngày rồi có lại vẫn ok.
/// </summary>
public class DeviceListenerDay
{
    public Guid StatusId { get; set; }
    public DateOnly Day { get; set; }
    public Guid UserId { get; set; }
    public bool AnyPermissionDenied { get; set; }
    public bool LastPermissionGranted { get; set; }
    public bool LastListenerConnected { get; set; }
    public DateTimeOffset LastReportedAt { get; set; }
    public int ReportCount { get; set; }

    public bool PermissionOk => !AnyPermissionDenied && LastListenerConnected;

    /// <summary>Trạng thái của lần báo cuối: thứ mà ngày không có lần báo kế thừa (S4).</summary>
    public bool LastStateOk => LastPermissionGranted && LastListenerConnected;

    public DeviceListenerStatus? Status { get; set; }
}

/// <summary>Số ví ở trạng thái <c>upload_gap</c> theo phiên bản app, ảnh chụp mỗi ngày (S2). Không có user: chỉ để đội phát triển thấy bản app nào đồng bộ hỏng.</summary>
public class ListenerUploadGapDaily
{
    public DateOnly Day { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public int Count { get; set; }
}
