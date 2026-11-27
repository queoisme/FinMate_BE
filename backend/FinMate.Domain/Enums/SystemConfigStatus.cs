namespace FinMate.Domain.Enums;

public enum SystemConfigStatus
{
    /// <summary>Phiên bản đang chạy: mỗi tài liệu có đúng một.</summary>
    Active,

    /// <summary>Phiên bản cũ, còn trong lịch sử để xem hoặc hoàn tác.</summary>
    Archived,

    /// <summary>Phase 95: bản nháp, người soạn còn sửa được.</summary>
    Draft,

    /// <summary>Đã gửi duyệt; mỗi phạm vi chỉ một bản ở trạng thái này.</summary>
    PendingApproval,

    /// <summary>Bị từ chối kèm ghi chú; kết thúc.</summary>
    Rejected,

    /// <summary>Người soạn bỏ, hoặc bị thay bằng bản "làm lại" (S1); kết thúc.</summary>
    Cancelled,
}
