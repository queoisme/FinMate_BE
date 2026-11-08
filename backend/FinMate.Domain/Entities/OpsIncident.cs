using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Một sự cố hệ thống. Lỗi giống nhau gom vào đúng một sự cố đang mở, nhờ đó một lần sập không sinh hàng trăm email. Không chứa dữ liệu người dùng.</summary>
public class OpsIncident
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public IncidentSource Source { get; set; }
    public string Service { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Tên kiểu lỗi, mã lỗi. Chỉ Admin xem được, không đưa vào email.</summary>
    public string? TechnicalDetail { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Null kèm <see cref="ResolvedAt"/> có giá trị nghĩa là hệ thống tự đóng.</summary>
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }
    public int NotificationCount { get; set; }
    public DateTimeOffset? ResolvedNotifiedAt { get; set; }
}

/// <summary>Trạng thái hiện tại của một dịch vụ. Lưu ở Postgres, không ở Redis, vì Redis có thể chính là thứ đang hỏng.</summary>
public class OpsServiceStatus
{
    public string Service { get; set; } = string.Empty;
    public MonitoredServiceState State { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
    public DateTimeOffset? LastHealthyAt { get; set; }

    /// <summary>Mã HTTP hoặc tên kiểu lỗi, không có nội dung phản hồi.</summary>
    public string? Detail { get; set; }
}

/// <summary>Một lỗi đã ghi nhận. Cố ý không có message của exception, stack trace hay giá trị tham số, vì chúng có thể chứa số tiền, token hoặc nội dung người dùng.</summary>
public class OpsErrorEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Service { get; set; } = string.Empty;
    public IncidentSource Source { get; set; }
    public string? ExceptionType { get; set; }
    public string? SourceContext { get; set; }
    public string? MessageTemplate { get; set; }
    public string? RequestPath { get; set; }
    public string? TraceId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}
