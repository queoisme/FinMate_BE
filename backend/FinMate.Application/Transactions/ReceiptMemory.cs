using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>
/// Khi nào lựa chọn danh mục của người dùng đáng được AI Service ghi nhớ cho lần quét sau.
///
/// Chỉ khi: giao dịch đến từ hóa đơn (<c>Source = Receipt</c>), có tên cửa hàng, và danh mục là danh mục HỆ THỐNG đã chọn.
/// Danh mục tự tạo của người dùng có slug riêng mà AI không biết (AI chỉ nhận slug hệ thống), thu nhập không phải hóa đơn.
/// </summary>
public static class ReceiptMemory
{
    private const string IncomeSlug = "income";

    public static bool ShouldLearn(TransactionSource source, Category? category, string? merchantName)
        => source == TransactionSource.Receipt
           && !string.IsNullOrWhiteSpace(merchantName)
           && category is { UserId: null } system
           && system.Slug != IncomeSlug;
}
