using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Phase 92 (S3): một giao dịch vừa được xác nhận (confirm, tạo tay, gộp). Nếu user có khoản định kỳ đang theo dõi cùng chiều thì xếp hàng một
/// lượt tracker cho riêng user đó, ngoài request, để kỳ đã trả và trạng thái hoàn thành cập nhật ngay thay vì chờ job 01:30.
/// Chỉ xếp hàng, không bao giờ ném: việc này không được làm hỏng một giao dịch đã lưu.
/// </summary>
public interface IRecurringTrackingTrigger
{
    Task TransactionConfirmedAsync(Guid userId, TransactionType type, CancellationToken ct = default);
}
