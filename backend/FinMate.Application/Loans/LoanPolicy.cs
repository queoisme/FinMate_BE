using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>
/// Luật thuần của sổ vay (Phase 90, chép từ <c>DebtPolicy</c> của Phase 86 cũ): chiều giao dịch (BR-DEBT-05), điều kiện gắn (BR-DEBT-08, D4), tổng
/// lưu sẵn và trạng thái (BR-DEBT-07, D5, D6, D16), tách phần lãi khi trả vượt gốc (S1). Chỉ đổi entity trong bộ nhớ; ném lỗi TRƯỚC khi đổi gì, nên
/// một lần bị từ chối không để lại nửa thay đổi. Mã lỗi theo Q7: trả vượt là <c>LOAN_REPAYMENT_EXCEEDS_REMAINING</c>, làm gốc &lt; đã trả là
/// <c>LOAN_PRINCIPAL_BELOW_PAID</c> (422, như mã cũ của <c>/loans</c>).
/// </summary>
public static class LoanPolicy
{
    /// <summary>Loại giao dịch mà một dòng phải có: cho vay thì giải ngân là tiền ra, trả là tiền vào; đi vay thì ngược lại.</summary>
    public static TransactionType TransactionTypeFor(LoanDirection direction, LoanEntryKind kind)
        => (direction == LoanDirection.Lend) == (kind == LoanEntryKind.Principal) ? TransactionType.Debit : TransactionType.Credit;

    public static void EnsureDirection(LoanDirection direction, LoanEntryKind kind, TransactionType type)
    {
        if (type != TransactionTypeFor(direction, kind))
        {
            throw new BusinessRuleException(LoanErrorCodes.DirectionMismatch, "Chiều tiền của giao dịch không khớp với khoản vay này.");
        }
    }

    /// <summary>
    /// BR-DEBT-08 và D4: chỉ giao dịch Debit/Credit đã xác nhận, còn sống, không có quan hệ hoàn/đảo, không bị gộp, không đang nghi trùng,
    /// không chia sẻ nhóm, không gắn quỹ nhóm, và chưa gắn khoản nào.
    /// </summary>
    /// <param name="beingConfirmed">D4: nháp đang được xác nhận kèm khoản thì được xét như đã xác nhận.</param>
    public static void EnsureEligible(Transaction transaction, bool alreadyLinked, bool beingConfirmed = false)
    {
        if (!IsEligible(transaction, beingConfirmed) || alreadyLinked)
        {
            throw new BusinessRuleException(LoanErrorCodes.TransactionNotEligible, "Giao dịch này không gắn được vào khoản vay.");
        }
    }

    /// <summary>Cùng điều kiện với <see cref="EnsureEligible"/>, không tính việc đã gắn: dùng để lọc ứng viên gợi ý.</summary>
    public static bool IsEligible(Transaction transaction, bool beingConfirmed = false)
        => transaction.DeletedAt is null
            && (transaction.Status == TransactionStatus.Confirmed || (beingConfirmed && transaction.Status == TransactionStatus.Draft))
            && transaction.TransactionType is TransactionType.Debit or TransactionType.Credit
            && transaction.Relation is null
            && transaction.MergedIntoTransactionId is null
            && transaction.DuplicateStatus != DuplicateStatus.Suspected
            && transaction.GroupId is null
            && transaction.GroupFundEntryId is null;

    /// <summary>
    /// S1: chia một khoản trả thành phần gốc và phần lãi. Không vượt còn lại → toàn bộ là gốc. Vượt còn lại thì chỉ nhận khi khoản có lãi suất,
    /// còn gốc để trả, và phần vượt ≤ lãi tạm tính + ngưỡng (<c>loans.interestToleranceCents</c>); phần vượt thành giao dịch lãi riêng. Ngoài ra → 422
    /// <c>LOAN_REPAYMENT_EXCEEDS_REMAINING</c>.
    /// </summary>
    public static (long PrincipalCents, long InterestCents) SplitRepayment(Loan loan, long amountCents, long accruedInterestCents, long toleranceCents)
    {
        EnsurePositive(amountCents);
        var remaining = loan.PrincipalCents - loan.RepaidCents;
        if (amountCents <= remaining)
        {
            return (amountCents, 0);
        }

        var excess = amountCents - remaining;
        if (loan.InterestRatePercentYear > 0 && remaining > 0 && excess <= Math.Max(0, accruedInterestCents) + Math.Max(0, toleranceCents))
        {
            return (remaining, excess);
        }

        throw new BusinessRuleException(LoanErrorCodes.Overpay, "Số tiền vượt quá số còn lại của khoản này.");
    }

    /// <summary>Thêm một dòng vào tổng. Trả vượt số còn lại → 422 <c>LOAN_REPAYMENT_EXCEEDS_REMAINING</c>.</summary>
    public static void Add(Loan loan, LoanEntryKind kind, long amountCents, DateTimeOffset now)
    {
        EnsurePositive(amountCents);
        var (principal, repaid) = Totals(loan, kind, amountCents);
        Apply(loan, principal, repaid, now);
    }

    /// <summary>
    /// Bớt một dòng khỏi tổng. Làm Σ giải ngân &lt; Σ trả → 422 <c>LOAN_PRINCIPAL_BELOW_PAID</c>. Trả true khi khoản thành rỗng (không còn giải ngân
    /// lẫn trả): caller xoá mềm khoản (D5).
    /// </summary>
    public static bool Remove(Loan loan, LoanEntryKind kind, long amountCents, DateTimeOffset now)
    {
        EnsurePositive(amountCents);
        var (principal, repaid) = Totals(loan, kind, -amountCents);
        Apply(loan, principal, repaid, now);
        return principal == 0 && repaid == 0;
    }

    /// <summary>Số tiền của giao dịch đã gắn đổi (D3): kiểm như bớt dòng cũ rồi thêm dòng mới, trong một bước.</summary>
    public static void ChangeAmount(Loan loan, LoanEntryKind kind, long oldCents, long newCents, DateTimeOffset now)
    {
        EnsurePositive(oldCents);
        EnsurePositive(newCents);
        var (principal, repaid) = Totals(loan, kind, newCents - oldCents);
        Apply(loan, principal, repaid, now);
    }

    /// <summary>BR-DEBT-10, D6, D16: đóng là <c>WrittenOff</c>, giữ nguyên số liệu. Lần hai không đổi gì; khoản đã trả xong thì 422.</summary>
    public static void WriteOff(Loan loan, DateTimeOffset now)
    {
        switch (loan.Status)
        {
            case LoanStatus.WrittenOff:
                return;
            case LoanStatus.Settled:
                throw new BusinessRuleException(LoanErrorCodes.AlreadySettled, "Khoản vay này đã trả xong.");
            default:
                loan.Status = LoanStatus.WrittenOff;
                loan.WrittenOffAt = now;
                loan.UpdatedAt = now;
                return;
        }
    }

    private static (long Principal, long Repaid) Totals(Loan loan, LoanEntryKind kind, long delta)
    {
        var principal = loan.PrincipalCents + (kind == LoanEntryKind.Principal ? delta : 0);
        var repaid = loan.RepaidCents + (kind == LoanEntryKind.Repayment ? delta : 0);
        if (repaid > principal && kind == LoanEntryKind.Repayment)
        {
            throw new BusinessRuleException(LoanErrorCodes.Overpay, "Số tiền vượt quá số còn lại của khoản này.");
        }

        if (repaid > principal)
        {
            throw new BusinessRuleException(LoanErrorCodes.PrincipalBelowPaid, "Số tiền gốc không được nhỏ hơn số đã trả.");
        }

        return (principal, repaid);
    }

    /// <summary>
    /// Ghi tổng mới và trạng thái theo BR-DEBT-07 / D6: còn lại 0 (và đã có giải ngân) → <c>Settled</c>; còn lại &gt; 0 → <c>Open</c>. Mọi thay đổi
    /// dòng trên khoản <c>WrittenOff</c> mở lại nó: tiền thật thắng một phán đoán cũ.
    /// </summary>
    private static void Apply(Loan loan, long principal, long repaid, DateTimeOffset now)
    {
        loan.PrincipalCents = principal;
        loan.RepaidCents = repaid;
        loan.UpdatedAt = now;
        loan.WrittenOffAt = null;

        if (principal > 0 && principal == repaid)
        {
            loan.SettledAt = loan.Status == LoanStatus.Settled ? loan.SettledAt ?? now : now;
            loan.Status = LoanStatus.Settled;
            return;
        }

        loan.Status = LoanStatus.Open;
        loan.SettledAt = null;
    }

    private static void EnsurePositive(long amountCents)
    {
        if (amountCents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents), "Số tiền phải lớn hơn 0.");
        }
    }
}
