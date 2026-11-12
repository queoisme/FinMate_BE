using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Refunds;

/// <summary>
/// Quy tắc thuần cho hoàn tiền / đảo giao dịch (Phase 81): khoản gốc nào nhận hoàn được, và nhận bao nhiêu. Không đọc DB:
/// tổng đã hoàn do caller đọc SAU khi đã khoá dòng khoản gốc, nếu không hai khoản hoàn đồng thời cùng vượt BR-RF-02.
/// </summary>
public static class RefundPolicy
{
    /// <summary>BR-RF-01, BR-RF-02, BR-RF-08, D4, D12. Không giới hạn thời gian: cửa sổ ngày chỉ dùng cho danh sách GỢI Ý.</summary>
    public static void EnsureLinkable(Transaction original, TransactionRelation relation, long amountCents, long alreadyRefundedCents)
    {
        if (!IsEligibleOriginal(original))
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.RefundOriginalNotEligible,
                "Chỉ hoàn hoặc đảo được khoản chi đã xác nhận, chưa bị đảo và không phải cho vay/mượn.");
        }

        if (relation == TransactionRelation.Reversal)
        {
            if (amountCents != original.AmountCents || alreadyRefundedCents > 0)
            {
                throw new BusinessRuleException(
                    TransactionErrorCodes.ReversalAmountMismatch,
                    "Đảo giao dịch phải bằng đúng số tiền gốc và khoản gốc chưa được hoàn phần nào. Hoàn một phần thì chọn hoàn tiền.");
            }

            return;
        }

        if (alreadyRefundedCents + amountCents > original.AmountCents)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.RefundExceedsOriginal,
                "Tổng tiền hoàn vượt quá số tiền của khoản chi gốc.");
        }
    }

    /// <summary>Khoản này có mang được quan hệ hoàn/đảo tới <paramref name="relatedTransactionId"/> không: chỉ tiền vào, và không trỏ vào chính nó.</summary>
    public static void EnsureCanCarryRelation(Transaction transaction, Guid? relatedTransactionId)
    {
        if (transaction.TransactionType != TransactionType.Credit || relatedTransactionId == transaction.Id)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.RefundNotLinkable,
                "Chỉ khoản tiền vào mới liên kết được làm hoàn tiền, và không thể liên kết với chính nó.");
        }
    }

    public static bool IsEligibleOriginal(Transaction original)
        => original.TransactionType == TransactionType.Debit
            && original.Status == TransactionStatus.Confirmed
            && !original.IsReversed
            && original.Category?.Slug != Category.LoanSlug;

    /// <summary>Phần còn hoàn được. Khoản đã đảo thì 0. Không âm kể cả khi dữ liệu cũ lệch.</summary>
    public static long RemainingRefundable(Transaction original, long alreadyRefundedCents)
        => original.IsReversed ? 0 : Math.Max(0, original.AmountCents - alreadyRefundedCents);
}
