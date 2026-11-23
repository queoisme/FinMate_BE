namespace FinMate.Application.Common.Exceptions;

public static class LoanErrorCodes
{
    public const string Invalid = "LOAN_INVALID";
    public const string Overpay = "LOAN_REPAYMENT_EXCEEDS_REMAINING";
    public const string PrincipalBelowPaid = "LOAN_PRINCIPAL_BELOW_PAID";

    /// <summary>Phase 90 (S2): dòng giải ngân đầu gắn giao dịch, đổi gốc ở giao dịch đó.</summary>
    public const string PrincipalLinked = "LOAN_PRINCIPAL_LINKED";

    /// <summary>Phase 90 (Q7): chiều giao dịch không khớp chiều khoản (BR-DEBT-05).</summary>
    public const string DirectionMismatch = "LOAN_DIRECTION_MISMATCH";

    /// <summary>Phase 90 (Q7): giao dịch không gắn được vào khoản (BR-DEBT-08, D4).</summary>
    public const string TransactionNotEligible = "LOAN_TRANSACTION_NOT_ELIGIBLE";

    /// <summary>Phase 90 (Q7): tạo khoản từ giao dịch có sẵn mà số gốc khác số tiền giao dịch (D18).</summary>
    public const string AmountMismatch = "LOAN_AMOUNT_MISMATCH";

    /// <summary>Phase 90 (Q7): xoá nợ một khoản đã trả xong (D6).</summary>
    public const string AlreadySettled = "LOAN_ALREADY_SETTLED";

    /// <summary>Phase 90 (Q7): giao dịch đã gắn khoản vay chỉ đổi được số tiền, ngày, mô tả; gỡ liên kết trước (D3, D7).</summary>
    public const string TransactionLinked = "TRANSACTION_LOAN_LINKED";
}
