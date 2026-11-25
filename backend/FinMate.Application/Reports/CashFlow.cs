using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Enums;

namespace FinMate.Application.Reports;

/// <summary>
/// Một giao dịch ĐÃ XÁC NHẬN, đủ để tính cả chi/thu (<see cref="NetFlow"/>) lẫn dòng tiền (Phase 90, Q16). <paramref name="IsLoan"/>: danh mục
/// <c>loan</c>. <paramref name="InGroupFund"/>: giao dịch cá nhân gắn dòng quỹ nhóm (Phase 85). <paramref name="Legs"/>: tác động số dư, LUÔN lấy từ
/// <see cref="TransferLegs"/> (chỗ duy nhất đọc hướng quỹ), không tự suy từ loại giao dịch.
/// </summary>
public sealed record CashFlowRow(
    DateTimeOffset TransactedAt,
    TransactionType Type,
    long AmountCents,
    bool IsLoan,
    TransactionRelation? Relation,
    bool IsReversed,
    Guid AccountId,
    bool InGroupFund,
    IReadOnlyList<WalletLeg> Legs)
{
    public static CashFlowRow From(Domain.Entities.Transaction t) => new(
        t.TransactedAt, t.TransactionType, t.AmountCents, LoanExclusion.IsLoan(t.Category), t.Relation, t.IsReversed, t.FinancialAccountId,
        t.GroupFundEntryId is not null, TransferLegs.Of(t));
}

/// <summary>
/// Chi tiêu thật và dòng tiền của báo cáo (Phase 90, Q16, D1–D3). Nguyên tắc: chi tiêu / thu nhập là tiền đã dùng hết hoặc thực sự kiếm được; tiền
/// đổi chỗ (chuyển giữa ví của mình, góp/rút quỹ nhóm, cho vay / đi vay / trả nợ) không phải chi hay thu nhưng vẫn hiện trong dòng tiền.
/// <para>
/// Bất biến, đúng cho từng ngày và cho mọi kỳ: <c>net = thu − chi − vayRa + vayVào − quỹRa + quỹVào − chuyểnRa + chuyểnVào + hoànVượtChi + đảoRòng</c>.
/// <c>net</c> tính THẲNG từ tác động số dư (<see cref="TransferLegs"/>), không cộng từ các trường khác, để bất biến là một phép kiểm thật.
/// </para>
/// </summary>
public static class CashFlowCalculator
{
    /// <summary>
    /// Gom theo ngày (giờ VN). <paramref name="accountId"/> null = mọi ví (chuyển ví nội bộ bù về 0, nên <c>Transfer*</c> luôn 0); có giá trị = chỉ
    /// phần tác động lên ví đó, kể cả giao dịch mà ví đó là ví ĐÍCH. Số lượng giao dịch chỉ đếm giao dịch có ví nguồn là ví đó, như trước.
    /// <paramref name="includeCashFlowOnlyDays"/>: giữ cả ngày chỉ có dòng tiền (vd. chỉ có cho vay). Mặc định bỏ như trước, vì dự báo chi tiêu và
    /// Agent coi "có điểm ngày" là "có hoạt động"; báo cáo tháng, dòng thời gian và tóm tắt ngày bật lên.
    /// </summary>
    public static List<DailySpendPoint> ByDay(IEnumerable<CashFlowRow> rows, Guid? accountId, bool includeCashFlowOnlyDays = true)
        => rows
            .GroupBy(r => VietnamTime.DateOf(r.TransactedAt))
            .Select(g => Day(g.Key, g, accountId))
            .Where(p => p.TransactionCount > 0 || (includeCashFlowOnlyDays && HasCashFlow(p)))
            .OrderBy(p => p.Date)
            .ToList();

    /// <summary>
    /// Cộng các ngày thành một kỳ. Chi của kỳ không bao giờ âm (như trước); phần bị chặn là <c>refundSurplusCents</c> (D2), để bất biến vẫn đúng.
    /// </summary>
    public static (long SpentCents, long IncomeCents, int Count, long RefundedCents, CashFlowDto CashFlow) Period(IEnumerable<DailySpendPoint> days)
    {
        var list = days.ToList();
        var netSpent = list.Sum(p => p.SpentCents);
        return (
            Math.Max(0, netSpent),
            list.Sum(p => p.IncomeCents),
            list.Sum(p => p.TransactionCount),
            list.Sum(p => p.RefundedCents),
            new CashFlowDto(
                list.Sum(p => p.CashNetCents),
                list.Sum(p => p.LoanOutflowCents),
                list.Sum(p => p.LoanInflowCents),
                list.Sum(p => p.GroupFundOutflowCents),
                list.Sum(p => p.GroupFundInflowCents),
                list.Sum(p => p.TransferOutflowCents),
                list.Sum(p => p.TransferInflowCents),
                Math.Max(0, -netSpent),
                list.Sum(p => p.ReversalNetCents)));
    }

    private static DailySpendPoint Day(DateOnly date, IEnumerable<CashFlowRow> rows, Guid? accountId)
    {
        long spent = 0, income = 0, refunded = 0, net = 0, loanOut = 0, loanIn = 0, fundOut = 0, fundIn = 0, transferOut = 0, transferIn = 0, reversal = 0;
        var count = 0;
        foreach (var r in rows)
        {
            var delta = r.Legs.Where(l => accountId is null || l.AccountId == accountId).Sum(l => l.DeltaCents);
            net += delta;
            var isSource = accountId is null || r.AccountId == accountId;
            switch (KindOf(r))
            {
                case Kind.Loan:
                    Split(delta, ref loanOut, ref loanIn);
                    break;
                case Kind.GroupFund:
                    Split(delta, ref fundOut, ref fundIn);
                    count += isSource ? 1 : 0;
                    break;
                case Kind.Transfer:
                    Split(delta, ref transferOut, ref transferIn);   // mọi ví: hai vế bù về 0
                    count += isSource ? 1 : 0;
                    break;
                case Kind.ReversalPair:
                    reversal += delta;   // D1: cặp đảo không vào chi/thu; cùng kỳ thì bù về 0
                    break;
                default:
                    var parts = NetFlow.Of(r.Type, r.Relation, r.IsReversed, r.AmountCents);
                    spent += parts.SpentCents;
                    income += parts.IncomeCents;
                    refunded += parts.RefundedCents;
                    count += parts.Counted ? 1 : 0;
                    break;
            }
        }

        return new DailySpendPoint(date, spent, income, count, refunded)
        {
            CashNetCents = net,
            LoanOutflowCents = loanOut,
            LoanInflowCents = loanIn,
            GroupFundOutflowCents = fundOut,
            GroupFundInflowCents = fundIn,
            TransferOutflowCents = transferOut,
            TransferInflowCents = transferIn,
            ReversalNetCents = reversal,
        };
    }

    private enum Kind
    {
        Ordinary,
        Loan,
        GroupFund,
        Transfer,
        ReversalPair,
    }

    private static Kind KindOf(CashFlowRow r) => r switch
    {
        { InGroupFund: true } => Kind.GroupFund,
        { Type: TransactionType.Transfer } => Kind.Transfer,
        { IsLoan: true } => Kind.Loan,
        { Type: TransactionType.Debit, IsReversed: true } => Kind.ReversalPair,
        { Type: TransactionType.Credit, Relation: TransactionRelation.Reversal } => Kind.ReversalPair,
        _ => Kind.Ordinary,
    };

    private static void Split(long delta, ref long outflow, ref long inflow)
    {
        if (delta < 0)
        {
            outflow += -delta;
        }
        else
        {
            inflow += delta;
        }
    }

    private static bool HasCashFlow(DailySpendPoint p)
        => p.CashNetCents != 0 || p.LoanOutflowCents != 0 || p.LoanInflowCents != 0 || p.GroupFundOutflowCents != 0 || p.GroupFundInflowCents != 0
           || p.TransferOutflowCents != 0 || p.TransferInflowCents != 0 || p.ReversalNetCents != 0;
}
