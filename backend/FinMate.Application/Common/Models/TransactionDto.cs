namespace FinMate.Application.Common.Models;

public record TransactionDto(
    Guid Id,
    Guid FinancialAccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    string? CategoryName,
    long AmountCents,
    string TransactionType,
    string Source,
    string Status,
    string? MerchantName,
    string? Description,
    DateTimeOffset TransactedAt,
    long? BalanceAfterCents,
    DateTimeOffset CreatedAt);

public record TransactionListDto(IReadOnlyList<TransactionDto> Items, string? NextCursor);

public record ParsedTransactionDto(
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    string? Description,
    DateTimeOffset? TransactedAt,
    string? CategorySlug);

/// <param name="OcrResult">
/// <c>"success"</c>, <c>"no_amount"</c> (đọc được chữ nhưng không thấy tổng tiền) hay
/// <c>"unreadable"</c> (ảnh không ra chữ nào). Ba trạng thái này dẫn tới ba lời nhắc khác
/// hẳn nhau cho người dùng — gộp lại thành một cờ thành/bại thì client chỉ còn cách nói
/// "thử lại" cho cả ba.
/// </param>
/// <param name="Confidence">
/// Luôn dưới 0,85. Docx phương thức 3 yêu cầu người dùng rà soát trước khi lưu, nên không
/// có ca nào của luồng này được rơi vào vùng tự động xác nhận một chạm.
/// </param>
public record ScannedReceiptDto(
    string OcrResult,
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    DateTimeOffset? TransactedAt,
    string? CategorySlug,
    double? Confidence);
