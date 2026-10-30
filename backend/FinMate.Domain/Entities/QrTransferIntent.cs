using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Ý định chuyển tiền được tạo khi người dùng quét QR của người nhận, TRƯỚC khi app ngân hàng mở ra. KHÔNG phải giao dịch và KHÔNG xác nhận tiền đã đi: tiền do app ngân hàng của người dùng chuyển,
/// FinMate không chạm vào. Chỉ khi thông báo thật của ngân hàng GỬI về (cùng ngân hàng, cùng số tiền, sau lúc quét trong một khoảng ngắn) thì bản ghi này mới được khớp và làm giàu giao dịch nháp bằng người
/// nhận + danh mục đã biết, thay vì để AI đoán từ nội dung chuyển khoản P2P (thường không có gì để đoán).
/// </summary>
public class QrTransferIntent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>BIN ngân hàng của người NHẬN (từ mã QR).</summary>
    public string ReceiverBankBin { get; set; } = string.Empty;

    public string ReceiverAccountNumber { get; set; } = string.Empty;

    /// <summary>Tên thụ hưởng nếu mã QR có ghi; chỉ để hiển thị, không bao giờ dùng để suy ra danh mục.</summary>
    public string? ReceiverName { get; set; }

    /// <summary>
    /// BIN ngân hàng của người GỬI: app ngân hàng người dùng chọn để mở. Đây là thứ phải so với thông báo (thông báo đến từ app của người gửi); bản thử trước so nhầm với ngân hàng người nhận nên chuyển
    /// khác ngân hàng không bao giờ khớp.
    /// </summary>
    public string SenderBankBin { get; set; } = string.Empty;

    /// <summary>Số tiền, cùng đơn vị với <c>transactions.amount_cents</c> (đồng nguyên). Bắt buộc: không có nó thì không khớp được với thông báo.</summary>
    public long AmountCents { get; set; }

    public string? Message { get; set; }

    /// <summary>Danh mục người dùng CHỦ ĐỘNG chọn lúc quét (hoặc đã có sẵn từ lần trước). Null = "Bỏ qua", giao dịch sẽ rơi về AI như bình thường.</summary>
    public Guid? CategoryId { get; set; }

    public QrTransferStatus Status { get; set; } = QrTransferStatus.Pending;
    public DateTimeOffset ScannedAt { get; set; }

    /// <summary>Giao dịch nháp đã được khớp. Khóa ngoại SET NULL: xóa giao dịch không được làm mất lịch sử quét.</summary>
    public Guid? MatchedTransactionId { get; set; }

    public DateTimeOffset? MatchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
