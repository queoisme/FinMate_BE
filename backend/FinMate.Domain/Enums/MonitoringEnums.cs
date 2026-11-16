namespace FinMate.Domain.Enums;

public enum IncidentSource
{
    Health,
    AppError,
    Job,
    Provider,
    Payment,

    /// <summary>Ngân hàng/ví có thể đã đổi mẫu thông báo (Phase 84, BR-LH-10).</summary>
    NotificationFormat,
}

/// <summary>Thứ tự có nghĩa: số càng lớn càng nặng.</summary>
public enum IncidentSeverity
{
    Normal,
    High,
    Critical,
}

public enum IncidentStatus
{
    Open,
    Acknowledged,
    Resolved,
}

public enum MonitoredServiceState
{
    Healthy,
    Unhealthy,

    /// <summary>Chưa cấu hình trên môi trường này, không phải lỗi.</summary>
    Disabled,

    /// <summary>Chưa đọc được trạng thái, ví dụ dịch vụ bản cũ chưa có API này. Không mở sự cố để deploy lệch phiên bản không báo động giả.</summary>
    Unknown,
}
