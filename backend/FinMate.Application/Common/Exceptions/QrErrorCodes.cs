namespace FinMate.Application.Common.Exceptions;

public static class QrErrorCodes
{
    /// <summary>BIN của ngân hàng nhận hoặc gửi không có trong danh mục ngân hàng VietQR (hoặc đã bị tắt).</summary>
    public const string BankUnsupported = "QR_BANK_UNSUPPORTED";

    /// <summary>Hủy một ý định đã khớp với giao dịch thật: hủy sẽ làm mất dấu người nhận của giao dịch đã tạo.</summary>
    public const string AlreadyMatched = "QR_TRANSFER_ALREADY_MATCHED";
}
