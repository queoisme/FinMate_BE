namespace FinMate.Domain.Enums;

/// <summary>Vòng đời của một ý định chuyển tiền qua QR. Chỉ ba trạng thái: hết hạn không lưu mà tính từ <c>scanned_at</c>, để không cần job dọn.</summary>
public enum QrTransferStatus
{
    /// <summary>Đã quét và mở app ngân hàng, chưa có thông báo ngân hàng nào xác nhận.</summary>
    Pending,

    /// <summary>Thông báo thật của ngân hàng đã khớp: giao dịch nháp tương ứng đã mang người nhận và danh mục.</summary>
    Matched,

    /// <summary>Người dùng tự hủy (quét nhầm, hoặc không mở được app ngân hàng).</summary>
    Cancelled,
}
