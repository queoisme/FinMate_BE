using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Việc nhạy cảm cần hai người: một người đề xuất, người khác duyệt. Lý do do người đề xuất viết nên không đưa vào audit.</summary>
public class OpsApprovalRequest
{
    public Guid Id { get; set; }
    public OpsApprovalType Type { get; set; }

    /// <summary>Tài khoản với <c>UnlockUser</c>, <c>CancelDeletion</c>; nhóm với <c>TransferGroupOwner</c>. Không có FK vì trỏ tới nhiều bảng.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Chủ nhóm mới, chỉ với <c>TransferGroupOwner</c>.</summary>
    public Guid? NewOwnerUserId { get; set; }
    public Guid? TicketId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? RequestedByUserId { get; set; }
    public OpsApprovalStatus Status { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Mã lỗi khi đã duyệt mà không thực hiện được, ví dụ đối tượng đã đổi trạng thái.</summary>
    public string? FailureCode { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CannedReply
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
