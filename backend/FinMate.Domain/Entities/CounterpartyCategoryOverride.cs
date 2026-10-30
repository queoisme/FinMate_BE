namespace FinMate.Domain.Entities;

/// <summary>
/// Bảng tra "người nhận (ngân hàng + số tài khoản) → danh mục" cho TỪNG người dùng. Không phải máy học: chỉ một khóa-giá-trị do người dùng chủ động chọn. Chỉ được ghi khi người dùng chọn tay (lúc quét,
/// hoặc sửa danh mục của giao dịch đã khớp QR), KHÔNG ghi khi họ chỉ xác nhận gợi ý của AI: cùng một người có thể nhận tiền vì lý do khác nhau mỗi lần, khóa cứng theo gợi ý sẽ gắn sai mãi.
/// </summary>
public class CounterpartyCategoryOverride
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Tên người nhận gần nhất người dùng thấy, để màn quản lý hiển thị được. Có thể null.</summary>
    public string? DisplayName { get; set; }

    public Guid CategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Category? Category { get; set; }
}
