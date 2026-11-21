using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Yêu cầu chờ chủ nhóm duyệt: hóa đơn hoặc chi/rút quỹ từ ngưỡng <c>require_approval_above_cents</c> do người không phải Owner tạo.
/// Khoản chờ CHƯA tồn tại ở bảng thật (hóa đơn, dòng quỹ), nên không vào quỹ, ngân sách, báo cáo hay sổ nợ; <see cref="PayloadJson"/> giữ nguyên
/// lệnh tạo, và duyệt là chạy lại đúng đường tạo bình thường dưới tên người yêu cầu.
/// </summary>
public class GroupApproval
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public GroupApprovalKind Kind { get; set; }
    public GroupApprovalStatus Status { get; set; } = GroupApprovalStatus.Pending;
    public Guid? RequestedByUserId { get; set; }
    public long AmountCents { get; set; }

    /// <summary>Tên hóa đơn hoặc ghi chú dòng quỹ, để hiện trong danh sách chờ.</summary>
    public string Title { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    /// <summary>Gửi lại cùng mã thì trả lại yêu cầu cũ thay vì tạo thêm (chỉ dòng quỹ có mã này).</summary>
    public Guid? ClientRequestId { get; set; }

    /// <summary>Id hóa đơn hoặc dòng quỹ được tạo khi duyệt.</summary>
    public Guid? ResultId { get; set; }

    public string? DecisionReason { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
