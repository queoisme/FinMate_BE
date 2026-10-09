namespace FinMate.Application.Transactions;

/// <summary>
/// Bật tắt quét tách nhóm theo món, đọc từ <c>Receipt:SplitByItemsEnabled</c> (biến môi trường <c>Receipt__SplitByItemsEnabled</c>).
/// Mặc định TẮT: tắt thì quét hóa đơn chạy y như trước, không gọi <c>/bill/classify-items</c>.
/// </summary>
public record ReceiptOptions(bool SplitByItemsEnabled);
