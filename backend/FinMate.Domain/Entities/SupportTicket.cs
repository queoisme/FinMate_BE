using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Một yêu cầu hỗ trợ của khách. Nội dung có thể chứa dữ liệu nhạy cảm nên không ghi vào log hay audit.</summary>
public class SupportTicket
{
    public Guid Id { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public SupportTicketCategory Category { get; set; }
    public SupportTicketPriority Priority { get; set; }
    public SupportTicketStatus Status { get; set; }
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset FirstResponseDueAt { get; set; }
    public DateTimeOffset ResolveDueAt { get; set; }
    public DateTimeOffset? FirstRespondedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Khách chấm 1 đến 5 sau khi ca đã giải quyết.</summary>
    public int? Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SupportTicketMessage
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public SupportMessageKind Kind { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Ảnh khách chủ động gửi vào ticket. Khác ảnh quét hóa đơn (không lưu ở đâu): ảnh này được mã hóa và tự xóa sau khi ca đóng.</summary>
public class SupportTicketAttachment
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid MessageId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];
    public byte[] Nonce { get; set; } = [];

    /// <summary>Đặt lúc ca đóng; null là ca còn mở.</summary>
    public DateTimeOffset? DeleteAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
