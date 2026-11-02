namespace FinMate.Domain.Enums;

/// <summary>
/// Vòng đời một khoản định kỳ. Chỉ <see cref="Active"/> được đưa vào dự báo. <see cref="Rejected"/> giữ lại (không xóa) để bộ phát hiện
/// không đề xuất lại cùng người nhận; <see cref="Cancelled"/> là user hủy khoản đã có.
/// </summary>
public enum RecurringStatus
{
    Suggested,
    Active,
    Paused,
    Rejected,
    Cancelled,
}
