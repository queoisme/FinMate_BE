using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Khách đồng ý cho đúng một nhân viên xem đúng phần dữ liệu trong một ca, có thời hạn.</summary>
public class SupportAccessGrant
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public SupportAccessScope Scopes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public SupportAccessStatus Status { get; set; }

    /// <summary>Ví khách chọn cho giao dịch nháp. Chỉ có khi đồng ý scope <see cref="SupportAccessScope.Draft"/>.</summary>
    public Guid? FinancialAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Quá hạn này mà khách chưa trả lời thì yêu cầu không còn đồng ý được.</summary>
    public DateTimeOffset RequestExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Mỗi lần nhân viên xem dữ liệu của khách. Khách đọc được bảng này. Không lưu nội dung đã xem.</summary>
public class SupportDataAccessLog
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid GrantId { get; set; }
    public Guid CustomerUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public SupportAccessScope Scope { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
