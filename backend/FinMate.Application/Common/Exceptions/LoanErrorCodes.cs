namespace FinMate.Application.Common.Exceptions;

public static class LoanErrorCodes
{
    public const string Invalid = "LOAN_INVALID";
    public const string Overpay = "LOAN_REPAYMENT_EXCEEDS_REMAINING";
    public const string PrincipalBelowPaid = "LOAN_PRINCIPAL_BELOW_PAID";
}
