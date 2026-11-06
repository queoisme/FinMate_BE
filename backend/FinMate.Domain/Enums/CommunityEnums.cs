namespace FinMate.Domain.Enums;

/// <summary>Trạng thái hiển thị của bài và bình luận. Tác giả tự xóa thì dùng <c>deleted_at</c>, không dùng trạng thái này.</summary>
public enum CommunityContentStatus
{
    Visible,

    /// <summary>Đủ số người báo cáo nên tự ẩn, chờ nhân viên xem.</summary>
    AutoHidden,

    /// <summary>Nhân viên đã gỡ.</summary>
    Removed,
}

public enum CommunityImageStatus
{
    Pending,
    Approved,
    Rejected,
}

public enum CommunityReportReason
{
    Scam,
    PersonalInfo,
    Harassment,
    Sexual,
    Spam,

    /// <summary>Đứng đầu hàng đợi ngay từ báo cáo đầu tiên và không tính vào việc tự ẩn: ẩn lời kêu cứu không giúp được người đang gặp khó khăn.</summary>
    SelfHarm,
    Other,
}

public enum CommunityReportPriority
{
    Normal,
    Urgent,
}

public enum CommunityReportStatus
{
    Open,
    Upheld,
    Dismissed,

    /// <summary>Nội dung đã bị tác giả xóa trước khi có người xử lý.</summary>
    Closed,
}
