namespace FinMate.Application.Common.Exceptions;

public static class LoanErrorCodes
{
    public const string Invalid = "LOAN_INVALID";
    public const string Overpay = "LOAN_REPAYMENT_EXCEEDS_REMAINING";
    public const string PrincipalBelowPaid = "LOAN_PRINCIPAL_BELOW_PAID";

    /// <summary>Phase 90 (S2): dòng giải ngân đầu gắn giao dịch, đổi gốc ở giao dịch đó.</summary>
    public const string PrincipalLinked = "LOAN_PRINCIPAL_LINKED";
}
