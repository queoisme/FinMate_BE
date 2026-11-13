namespace FinMate.Domain.Entities;

/// <summary>
/// Bảng tra "tên merchant (đã chuẩn hoá) → danh mục" cho TỪNG người dùng — dùng cho notification
/// ngân hàng KHÔNG qua luồng QR (§7 priority #1, §16.5). Khác <see cref="CounterpartyCategoryOverride"/>:
/// bảng đó khóa theo (BankBin, AccountNumber) có cấu trúc, chỉ QR flow có dữ liệu này; notification
/// thường chỉ có tên merchant dạng text tự do nên khóa theo text, kém chính xác hơn nhưng khả thi.
/// Chỉ ghi khi người dùng tự chọn/sửa danh mục lúc xác nhận, KHÔNG ghi khi chỉ chấp nhận gợi ý AI.
/// </summary>
public class MerchantCategoryOverride
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Đã chuẩn hoá qua <c>MerchantCategoryKey.Normalize</c> — khóa tra cứu.</summary>
    public string MerchantKey { get; set; } = string.Empty;

    /// <summary>Tên merchant gốc gần nhất (chưa chuẩn hoá), để màn quản lý hiển thị được.</summary>
    public string? DisplayName { get; set; }

    public Guid CategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
}
