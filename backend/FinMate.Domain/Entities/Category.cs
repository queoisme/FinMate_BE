namespace FinMate.Domain.Entities;

public class Category
{
    /// <summary>
    /// "Cho vay / Mượn": cả tiền cho người khác mượn (Debit) lẫn tiền mượn/được trả nợ (Credit).
    /// Số dư ví vẫn tính, nhưng KHÔNG tính vào tổng chi, tổng thu, ngân sách và dự báo: đó là tiền sẽ về lại (hoặc phải trả lại), không phải chi tiêu hay thu nhập.
    /// </summary>
    public const string LoanSlug = "loan";

    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? IconName { get; set; }

    public const string KindExpense = "expense";
    public const string KindIncome = "income";

    /// <summary>Màu nhận diện dạng <c>#RRGGBB</c> (chữ hoa), null = app dùng màu mặc định theo icon. Chọn từ bảng màu trong cấu hình hệ thống nhưng không bắt buộc nằm trong bảng.</summary>
    public string? Color { get; set; }

    /// <summary><see cref="KindExpense"/> hoặc <see cref="KindIncome"/>: để app lọc "Khoản chi tiêu / Khoản thu nhập". Chưa chặn việc dùng danh mục với loại giao dịch khác.</summary>
    public string Kind { get; set; } = KindExpense;

    /// <summary>Nhóm cha: luôn là một danh mục HỆ THỐNG (không lồng danh mục riêng vào nhau), để báo cáo gộp được theo nhóm.</summary>
    public Guid? ParentCategoryId { get; set; }

    /// <summary>Hạn mức ngân sách GỢI Ý mỗi tháng (đồng). Chỉ là gợi ý hiển thị: không tự tạo ngân sách.</summary>
    public long? SuggestedBudgetCents { get; set; }

    /// <summary>Đưa vào danh sách yêu thích của chủ danh mục. Chỉ có nghĩa với danh mục riêng (một người dùng); danh mục hệ thống dùng chung nên không đặt cờ này.</summary>
    public bool IsFavorite { get; set; }

    public bool IsSystem { get; set; }

    /// <summary>
    /// Danh mục còn được chọn khi tạo giao dịch mới hay không.
    ///
    /// Tách khỏi <see cref="DeletedAt"/> có chủ ý: <c>deleted_at</c> nằm trong global query
    /// filter, nên "tắt" bằng soft delete sẽ làm chi tiêu CŨ của danh mục đó rơi khỏi
    /// category-breakdown (báo cáo đi qua navigation <c>Transaction.Category</c>) mà không
    /// báo lỗi ở đâu. Danh mục tắt vẫn hiển thị đầy đủ trong lịch sử.
    /// </summary>
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public User? User { get; set; }
    public Category? ParentCategory { get; set; }
}
