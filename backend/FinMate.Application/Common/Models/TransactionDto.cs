using FinMate.Application.Transactions;

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
/// <param name="ScanId">
/// Mã bản ghi bóc tách của lần quét này (<c>receipt_scans</c>). Gửi lại thành <c>receiptScanId</c> khi lưu giao dịch (<c>POST /transactions</c>,
/// <c>source = Receipt</c>) để nối lần quét với giao dịch. Thêm vào cuối nên client cũ vẫn đọc được; client chưa dùng thì bỏ qua.
/// </param>
/// <param name="Groups">
/// Các nhóm khi bill được tách theo món (lớn nhất trước); null khi không tách. Chỉ tách khi có từ 2 nhóm có danh mục. Có nhóm thì
/// <c>AmountCents</c> vẫn là tổng bill và <c>CategorySlug</c> là nhóm CÓ danh mục lớn nhất (không bao giờ null khi tách được), nên client cũ không vỡ.
/// </param>
public record ScannedReceiptDto(
    string OcrResult,
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    DateTimeOffset? TransactedAt,
    string? CategorySlug,
    double? Confidence,
    Guid? ScanId = null,
    IReadOnlyList<ReceiptGroup>? Groups = null,
    Guid? PossibleDuplicateTransactionId = null);
