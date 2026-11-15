namespace FinMate.Domain.Enums;

/// <summary>Loại dòng quỹ. Lưu dạng TEXT snake_case; CHECK sinh từ enum.</summary>
public enum GroupFundEntryKind
{
    /// <summary>Góp vào quỹ (+). Chỉ cho chính mình.</summary>
    Contribution,

    /// <summary>Chi từ quỹ (−). Tính vào ngân sách và báo cáo NHÓM, không vào sổ chi tiêu cá nhân (BR-FUND-10/11).</summary>
    Expense,

    /// <summary>Rút về ví cá nhân / chia lại (−). Chỉ cho chính mình.</summary>
    Withdrawal,

    /// <summary>Điều chỉnh (±), cần <c>ManageFunds</c> và ghi chú.</summary>
    Adjustment,
}

public enum GroupFundEntryOrigin
{
    Created,

    /// <summary>Chuyển từ một khoản chi đã xác nhận của người ghi (S4a).</summary>
    Converted,
}

/// <summary>Hướng tiền của giao dịch cá nhân gắn quỹ, so với ví của người đó. Chỉ <c>TransferLegs</c> được đọc giá trị này.</summary>
public enum GroupFundDirection
{
    /// <summary>Tiền ra khỏi ví (góp, chi).</summary>
    Out,

    /// <summary>Tiền vào ví (rút).</summary>
    In,
}
