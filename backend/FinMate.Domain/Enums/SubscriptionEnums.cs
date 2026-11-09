namespace FinMate.Domain.Enums;

public enum SubscriptionCycle
{
    Monthly,
    Yearly,
}

public enum SubscriptionOrderStatus
{
    /// <summary>Đã tạo, chưa có tiền về.</summary>
    Pending,
    Paid,

    /// <summary>SePay báo đơn đã hủy. Tiền vẫn có thể về sau đó, khi ấy đơn chuyển sang <see cref="Paid"/>.</summary>
    Cancelled,

    /// <summary>Quá hạn mà chưa trả. Tiền vẫn có thể về sau đó, khi ấy đơn chuyển sang <see cref="Paid"/>.</summary>
    Expired,

    /// <summary>Giao dịch đã trả bị hủy phía cổng thanh toán.</summary>
    Voided,

    /// <summary>Tiền về nhưng không cấp được quyền lợi tự động (lệch số tiền, không còn người dùng). Cần người xử lý.</summary>
    NeedsReview,
}
