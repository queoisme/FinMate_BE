namespace FinMate.Domain.Entities;

public enum LoanDirection
{
    /// <summary>Mình đi vay: nợ cần trả.</summary>
    Borrow,

    /// <summary>Mình cho vay: tiền cần thu hồi.</summary>
    Lend,
}

/// <summary>
/// Sổ theo dõi một khoản vay/cho vay của người dùng. Số đã trả = tổng <see cref="LoanRepayment"/>; "còn lại" = gốc − đã trả (lãi chỉ lưu để hiển thị, chưa tự tính).
/// Tạo khoản vay không đụng ví; chỉ khi ghi một lần trả kèm ví thì mới sinh giao dịch (danh mục "Cho vay / Mượn", bị loại khỏi tổng thu/chi).
/// </summary>
public class Loan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public LoanDirection Direction { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public long PrincipalCents { get; set; }

    /// <summary>Lãi suất %/năm, 0–100. Chỉ để hiển thị.</summary>
    public decimal InterestRatePercentYear { get; set; }

    /// <summary>Ngày bắt đầu vay: mốc tính lãi tạm tính.</summary>
    public DateOnly StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>Đã nhắc đến hạn/quá hạn lần này; đổi hạn cuối thì xóa để nhắc lại.</summary>
    public DateTimeOffset? DueNotifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<LoanRepayment> Repayments { get; set; } = [];
}

public class LoanRepayment
{
    public Guid Id { get; set; }
    public Guid LoanId { get; set; }
    public long AmountCents { get; set; }
    public DateTimeOffset PaidAt { get; set; }

    /// <summary>Giao dịch ví sinh ra từ lần trả này; null = chỉ ghi sổ, không đụng ví.</summary>
    public Guid? TransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
