namespace FinMate.Domain.Entities;

/// <summary>
/// Nối một nhóm (theo danh mục AI đề xuất) của một lần quét với giao dịch người dùng đã lưu từ nhóm đó.
/// Thay cột <c>receipt_scans.transaction_id</c>: một hóa đơn tách được thành nhiều giao dịch, mỗi nhóm một giao dịch.
/// </summary>
public class ReceiptScanLink
{
    /// <summary>Khóa chính: một giao dịch đến từ tối đa một lần quét.</summary>
    public Guid TransactionId { get; set; }

    public Guid ScanId { get; set; }

    /// <summary>Slug nhóm AI đề xuất; <see cref="UnknownGroup"/> khi nhóm không có slug.</summary>
    public string GroupCategorySlug { get; set; } = UnknownGroup;

    public const string UnknownGroup = "unknown";

    public ReceiptScan? Scan { get; set; }
    public Transaction? Transaction { get; set; }
}
