namespace FinMate.Domain.Enums;

public enum DataDeletionStatus
{
    /// <summary>Đang đếm ngược 30 ngày. <c>DataDeletionJob</c> chỉ đụng vào trạng thái này.</summary>
    Pending,

    /// <summary>Đã xoá cứng. Không quay lại được — dữ liệu không còn.</summary>
    Processed,

    /// <summary>Admin đã dừng lại trước hạn; tài khoản được khôi phục.</summary>
    Cancelled,
}
