namespace FinMate.Domain.Enums;

/// <summary>Loại thông báo gửi cho đội vận hành trên web vận hành. Cột TEXT dạng snake_case; mỗi loại bật/tắt được ở nhóm <c>opsNotifications</c> của cấu hình hệ thống.</summary>
public enum OpsNotificationType
{
    /// <summary>Khách vừa tạo yêu cầu hỗ trợ. Gửi cho người điều phối ticket.</summary>
    TicketNew,

    /// <summary>Ticket vừa được giao cho bạn.</summary>
    TicketAssigned,

    /// <summary>Ca vừa được chuyển cấp, chờ Manager rà soát.</summary>
    TicketEscalated,

    /// <summary>Có đề xuất việc nhạy cảm chờ người thứ hai duyệt.</summary>
    ApprovalPending,

    /// <summary>Đề xuất của bạn đã được duyệt hoặc bị từ chối.</summary>
    ApprovalDecided,

    /// <summary>Sự cố hệ thống mức Cao hoặc Nghiêm trọng chưa có người nhận.</summary>
    IncidentOpened,

    /// <summary>Có báo cáo nội dung cộng đồng mới.</summary>
    CommunityReport,

    /// <summary>Có ảnh cộng đồng chờ duyệt.</summary>
    CommunityImagePending,

    /// <summary>Có yêu cầu xóa dữ liệu mới.</summary>
    DataDeletionRequested,
}
