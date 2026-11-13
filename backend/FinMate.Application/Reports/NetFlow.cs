using FinMate.Domain.Enums;

namespace FinMate.Application.Reports;

/// <summary>Phần đóng góp của MỘT giao dịch đã xác nhận vào tổng chi/thu/hoàn của báo cáo. <c>Counted</c> = có tính vào số lượng giao dịch.</summary>
public readonly record struct FlowParts(long SpentCents, long IncomeCents, long RefundedCents, bool Counted);

/// <summary>
/// Quy tắc chung của mọi báo cáo cộng trong bộ nhớ (Phase 81, BR-RF-03/06/08): chi = chi − hoàn (hoàn có số âm ở đây), thu KHÔNG gồm hoàn,
/// hoàn đếm riêng; cặp đảo (khoản chi đã đảo + lệnh đảo) bị loại khỏi mọi con số kể cả số lượng, nhưng vẫn hiện trong lịch sử.
/// Cho vay/mượn do caller lọc ở truy vấn như trước. Chuyển khoản nội bộ không thuộc chi hay thu, vẫn được đếm như trước.
/// Tổng hợp bằng SQL dùng <see cref="Budgets.BudgetSpendScope"/>, cùng ý nghĩa.
/// </summary>
public static class NetFlow
{
    public static FlowParts Of(TransactionType type, TransactionRelation? relation, bool isReversed, long amountCents)
        => type switch
        {
            TransactionType.Debit when isReversed => new FlowParts(0, 0, 0, false),
            TransactionType.Debit => new FlowParts(amountCents, 0, 0, true),
            TransactionType.Credit when relation == TransactionRelation.Reversal => new FlowParts(0, 0, 0, false),
            TransactionType.Credit when relation == TransactionRelation.Refund => new FlowParts(-amountCents, 0, amountCents, true),
            TransactionType.Credit => new FlowParts(0, amountCents, 0, true),
            _ => new FlowParts(0, 0, 0, true),
        };
}
