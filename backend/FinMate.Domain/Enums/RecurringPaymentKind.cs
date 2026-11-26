namespace FinMate.Domain.Enums;

/// <summary>Một kỳ đã trả của khoản định kỳ được ghi nhận thế nào (Phase 92).</summary>
public enum RecurringPaymentKind
{
    /// <summary>Tracker tự khớp một giao dịch với kỳ hạn (BR-INS-03).</summary>
    Matched,

    /// <summary>User đánh dấu tay, có hoặc không có giao dịch (BR-INS-04).</summary>
    Manual,

    /// <summary>Lần tất toán trước hạn (BR-INS-08); tính một kỳ trả, không ép số tiền.</summary>
    Settlement,
}
