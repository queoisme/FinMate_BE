namespace FinMate.Application.QrTransfers;

/// <param name="CanMatchNotifications">FinMate đọc được thông báo của ngân hàng này. Chỉ khi chọn nó làm ngân hàng GỬI thì giao dịch sau này mới tự khớp với lần quét.</param>
public record QrBankDto(string Bin, string Name, string AppCode, bool CanMatchNotifications);

/// <param name="IsExpired">Đang chờ nhưng đã quá <see cref="QrTransferService.PendingLifetime"/> kể từ lúc quét: gần như chắc chắn không bao giờ khớp. Tính lúc đọc, không lưu.</param>
public record QrTransferDto(
    Guid Id,
    string ReceiverBankBin,
    string ReceiverBankName,
    string ReceiverAccountNumber,
    string? ReceiverName,
    string SenderBankBin,
    string SenderBankName,
    long AmountCents,
    string? Message,
    Guid? CategoryId,
    string? CategoryName,
    string Status,
    bool IsExpired,
    DateTimeOffset ScannedAt,
    DateTimeOffset? MatchedAt,
    Guid? MatchedTransactionId);

public record CounterpartyOverrideDto(
    Guid Id,
    string BankBin,
    string BankName,
    string AccountNumber,
    string? DisplayName,
    Guid CategoryId,
    string CategoryName,
    DateTimeOffset UpdatedAt);

/// <param name="UserId">Lấy từ JWT, không bao giờ từ thân yêu cầu.</param>
/// <param name="CategoryId">Danh mục người dùng chọn tay lúc quét. Null thì dùng danh mục đã nhớ cho người nhận này (nếu có), không có nữa thì để AI đoán như thường.</param>
public record CreateQrTransferCommand(
    Guid UserId,
    string ReceiverBankBin,
    string ReceiverAccountNumber,
    string? ReceiverName,
    string SenderBankBin,
    long AmountCents,
    string? Message,
    Guid? CategoryId);
