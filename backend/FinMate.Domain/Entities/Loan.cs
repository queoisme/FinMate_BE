namespace FinMate.Domain.Entities;

public enum LoanDirection
{
    /// <summary>Mình đi vay: nợ cần trả.</summary>
    Borrow,

    /// <summary>Mình cho vay: tiền cần thu hồi.</summary>
    Lend,
}

/// <summary>Trạng thái khoản vay (Phase 90): <c>Settled</c> khi đã trả hết gốc, <c>WrittenOff</c> khi người dùng xoá nợ.</summary>
public enum LoanStatus
{
    Open,
    Settled,
    WrittenOff,
}

/// <summary>Loại dòng trong <c>loan_repayments</c> (Phase 90): giải ngân (tăng gốc) hoặc trả (giảm còn lại).</summary>
public enum LoanEntryKind
{
    Principal,
    Repayment,
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

    /// <summary>Phase 90: lưu sẵn = Σ dòng trả còn sống. <see cref="PrincipalCents"/> từ nay = Σ dòng giải ngân còn sống (Q5).</summary>
    public long RepaidCents { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Open;
    public string? Note { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public DateTimeOffset? WrittenOffAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? ClientRequestId { get; set; }

    /// <summary>Mọi dòng: giải ngân và trả (tên giữ từ trước Phase 90). Đọc qua <see cref="Live"/> để bỏ dòng đã xoá.</summary>
    public List<LoanRepayment> Repayments { get; set; } = [];

    public IEnumerable<LoanRepayment> Live(LoanEntryKind kind) => Repayments.Where(r => r.Kind == kind && r.DeletedAt is null);
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

    /// <summary>Phase 90: dòng giải ngân hay dòng trả. Dữ liệu trước Phase 90 đều là dòng trả.</summary>
    public LoanEntryKind Kind { get; set; } = LoanEntryKind.Repayment;

    /// <summary>Phase 90 (S1, Q14): giao dịch lãi tách ra khi trả vượt gốc còn lại trong ngưỡng lãi.</summary>
    public Guid? InterestTransactionId { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? ClientRequestId { get; set; }
}
