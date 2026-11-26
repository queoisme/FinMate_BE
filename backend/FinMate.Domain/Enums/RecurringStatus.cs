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

    /// <summary>
    /// Khoản có kỳ cuối đã trả đủ số kỳ, đã qua ngày kết thúc, hoặc đã tất toán sớm (Phase 92, BR-INS-06/08). Không chiếu dự báo, không nhắc hạn;
    /// không giữ chỗ trong ràng buộc duy nhất theo người nhận (BR-INS-09).
    /// </summary>
    Completed,
}
