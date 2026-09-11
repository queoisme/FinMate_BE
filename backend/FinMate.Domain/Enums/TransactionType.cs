namespace FinMate.Domain.Enums;

public enum TransactionType
{
    Debit,
    Credit,

    /// <summary>
    /// Chuyển tiền giữa 2 ví của cùng user (rút ATM, nạp ví điện tử). KHÔNG phải chi tiêu và
    /// KHÔNG phải thu nhập — chỉ dịch chuyển số dư, nên phải bị loại khỏi mọi phép cộng dồn
    /// ngân sách/báo cáo/dự báo. Xem ARCHITECTURE.md §0 quyết định #1.
    /// </summary>
    Transfer,
}
