using FinMate.Application.Budgets;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Gửi cảnh báo ngân sách, sau khi đã kiểm tra tuỳ chọn thông báo của người dùng.
///
/// Một chỗ duy nhất cho cả hai đường sinh cảnh báo (tức thì sau giao dịch, và job theo giờ),
/// nên quy tắc "tôn trọng PushEnabled/BudgetAlertsEnabled" không phải viết hai lần rồi lệch.
/// </summary>
public interface IBudgetAlertNotifier
{
    /// <summary>
    /// Gọi SAU khi thay đổi đã được lưu. Gửi trước khi lưu là báo cho người dùng về một giao
    /// dịch có thể bị rollback ngay sau đó.
    /// </summary>
    /// <summary>
    /// Người dùng có nhận cảnh báo ngân sách không. Hỏi TRƯỚC khi đánh dấu mốc đã gửi —
    /// đánh dấu rồi mới phát hiện họ tắt thông báo là mất hẳn mốc đó.
    ///
    /// Kết quả được nhớ trong phạm vi scope: một lượt chạy job mang cảnh báo của nhiều budget
    /// thuộc cùng một người.
    /// </summary>
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default);

    Task SendAsync(IReadOnlyList<BudgetAlert> alerts, CancellationToken ct = default);
}
