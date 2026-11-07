namespace FinMate.Domain.Entities;

/// <summary>
/// Luồng chính: <c>Open → Triaged → Responded → Resolved</c>. Ngoại lệ: <c>Triaged → Escalated → ManagerReview → Resolved | Rejected</c>. Người dùng phản đối kết quả (đã trả lời, đã giải quyết hoặc bị từ chối)
/// thì <c>Reopened</c>, rồi nhân sự phân loại lại (<c>Reopened → Triaged</c>). Chuyển trạng thái nào hợp lệ và ai được chuyển: xem <c>TicketStateMachine</c>.
/// </summary>
public enum SupportTicketStatus
{
    Open,
    Triaged,
    Responded,
    Resolved,
    Escalated,
    ManagerReview,
    Rejected,
    Reopened,
}

/// <summary>Khiếu nại / yêu cầu hỗ trợ của người dùng. Chỉ chủ khiếu nại và nhân sự có vai trò phù hợp thấy được; mọi truy vấn của người dùng lọc theo <c>userId</c>.</summary>
public class SupportTicket
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;

    /// <summary>Câu trả lời nhân sự đã GỬI cho người dùng (gợi ý của trợ lý không bao giờ vào đây cho tới khi nhân sự bấm gửi).</summary>
    public string? Reply { get; set; }

    /// <summary>Ghi chú của người quyết định cuối (Resolved / Rejected sau khi leo thang).</summary>
    public string? ResolutionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
}
