using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Reports;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

/// <summary>
/// Dòng tiền của báo cáo (Phase 90, Q16, D1–D3). Bất biến:
/// <c>net = thu − chi − vayRa + vayVào − quỹRa + quỹVào − chuyểnRa + chuyểnVào + hoànVượtChi + đảoRòng</c>, với <c>net</c> tính thẳng từ tác động số dư.
/// </summary>
public class CashFlowCalculatorTests
{
    private static readonly Guid Vcb = Guid.NewGuid();
    private static readonly Guid Momo = Guid.NewGuid();
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid[] Wallets = [Vcb, Momo, Cash];

    private static readonly DateOnly OctDay = new(2026, 10, 30);
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private static readonly DateOnly Nov2 = new(2026, 11, 2);

    private static DateTimeOffset At(DateOnly day, int hour = 10) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(7));

    private static CashFlowRow Row(
        DateOnly day, TransactionType type, long amount, Guid account, bool loan = false, TransactionRelation? relation = null, bool reversed = false,
        Guid? counter = null, bool fund = false, GroupFundDirection? fundDirection = null)
        => new(At(day), type, amount, loan, relation, reversed, account, fund,
            FinMate.Application.Transactions.TransferLegs.Of(type, account, counter, amount, fundDirection));

    /// <summary>Đủ mọi loại: chi, thu, hoàn cùng kỳ và hoàn khác kỳ vượt chi, cặp đảo cùng kỳ và khác kỳ, chuyển ví kèm phí, vay / trả nợ, góp / rút quỹ, lãi vay.</summary>
    private static List<CashFlowRow> Everything() =>
    [
        // Tháng 10: khoản chi sẽ bị đảo ở tháng 11, khoản chi sẽ được hoàn ở tháng 11.
        Row(OctDay, TransactionType.Debit, 400_000, Vcb, reversed: true),
        Row(OctDay, TransactionType.Debit, 1_000_000, Vcb),
        // Tháng 11
        Row(Nov1, TransactionType.Debit, 300_000, Vcb),                                                   // chi
        Row(Nov1, TransactionType.Credit, 15_000_000, Vcb),                                               // thu
        Row(Nov1, TransactionType.Debit, 200_000, Momo, reversed: true),                                  // cặp đảo cùng kỳ: gốc
        Row(Nov2, TransactionType.Credit, 200_000, Momo, relation: TransactionRelation.Reversal),         // cặp đảo cùng kỳ (khác ngày): lệnh đảo
        Row(Nov2, TransactionType.Credit, 400_000, Vcb, relation: TransactionRelation.Reversal),          // lệnh đảo cho khoản gốc tháng 10
        Row(Nov1, TransactionType.Credit, 100_000, Vcb, relation: TransactionRelation.Refund),            // hoàn cùng kỳ
        Row(Nov2, TransactionType.Credit, 1_000_000, Momo, relation: TransactionRelation.Refund),         // hoàn cho khoản tháng 10, vượt chi trong ngày
        Row(Nov1, TransactionType.Transfer, 2_000_000, Vcb, counter: Momo),                               // chuyển ví nội bộ
        Row(Nov1, TransactionType.Debit, 3_300, Vcb),                                                     // phí chuyển ví (khoản chi thường)
        Row(Nov1, TransactionType.Debit, 5_000_000, Vcb, loan: true),                                     // cho vay
        Row(Nov2, TransactionType.Credit, 2_000_000, Cash, loan: true),                                   // được trả nợ
        Row(Nov2, TransactionType.Credit, 3_000_000, Vcb, loan: true),                                    // đi vay
        Row(Nov2, TransactionType.Debit, 1_000_000, Cash, loan: true),                                    // trả nợ
        Row(Nov1, TransactionType.Transfer, 500_000, Momo, fund: true, fundDirection: GroupFundDirection.Out),   // góp quỹ
        Row(Nov2, TransactionType.Transfer, 150_000, Cash, fund: true, fundDirection: GroupFundDirection.In),    // rút quỹ
        Row(Nov2, TransactionType.Debit, 60_000, Vcb),                                                    // lãi vay phải trả (chi thật, S1)
        Row(Nov2, TransactionType.Credit, 25_000, Cash),                                                  // tiền lãi nhận được (thu thật, S1)
    ];

    private static long Balance(IEnumerable<CashFlowRow> rows, Guid? account)
        => rows.Sum(r => r.Legs.Where(l => account is null || l.AccountId == account).Sum(l => l.DeltaCents));

    private static void AssertInvariant(long spent, long income, CashFlowDto c, string because)
        => c.NetCents.Should().Be(
            income - spent - c.LoanOutflowCents + c.LoanInflowCents - c.GroupFundOutflowCents + c.GroupFundInflowCents
            - c.TransferOutflowCents + c.TransferInflowCents + c.RefundSurplusCents + c.ReversalNetCents,
            because);

    public static TheoryData<int> Scopes => new() { -1, 0, 1, 2 };   // -1 = mọi ví, còn lại = chỉ số ví

    [Theory]
    [MemberData(nameof(Scopes))]
    public void TheInvariantHolds_ForEveryDay_EveryMonth_AndTheNetIsTheRealBalanceChange(int scope)
    {
        Guid? account = scope < 0 ? null : Wallets[scope];
        var rows = Everything().Where(r => account is null || r.Legs.Any(l => l.AccountId == account)).ToList();

        var days = CashFlowCalculator.ByDay(rows, account);

        foreach (var day in days)
        {
            var (spent, income, _, _, c) = CashFlowCalculator.Period([day]);
            AssertInvariant(spent, income, c, $"ngày {day.Date}");
            c.NetCents.Should().Be(Balance(rows.Where(r => DateOnly.FromDateTime(r.TransactedAt.DateTime) == day.Date), account), $"ngày {day.Date}");
        }

        foreach (var month in days.GroupBy(d => (d.Date.Year, d.Date.Month)))
        {
            var (spent, income, _, _, c) = CashFlowCalculator.Period(month);
            AssertInvariant(spent, income, c, $"tháng {month.Key}");
            spent.Should().BeGreaterThanOrEqualTo(0);
            c.NetCents.Should().Be(Balance(rows.Where(r => (r.TransactedAt.Year, r.TransactedAt.Month) == month.Key), account), $"tháng {month.Key}");
        }
    }

    [Fact]
    public void AllWallets_November_HasTheExpectedParts()
    {
        var november = CashFlowCalculator.ByDay(Everything(), null).Where(d => d.Date.Month == 11);

        var (spent, income, _, refunded, c) = CashFlowCalculator.Period(november);

        spent.Should().Be(0, "chi gộp 363.300 nhỏ hơn hoàn 1.100.000");
        c.RefundSurplusCents.Should().Be(1_100_000 - (300_000 + 3_300 + 60_000));
        refunded.Should().Be(1_100_000);
        income.Should().Be(15_000_000 + 25_000);
        c.LoanOutflowCents.Should().Be(6_000_000);
        c.LoanInflowCents.Should().Be(5_000_000);
        c.GroupFundOutflowCents.Should().Be(500_000);
        c.GroupFundInflowCents.Should().Be(150_000);
        c.TransferOutflowCents.Should().Be(0, "mọi ví: chuyển ví nội bộ bù về 0");
        c.TransferInflowCents.Should().Be(0);
        c.ReversalNetCents.Should().Be(400_000, "lệnh đảo tháng 11 cho khoản gốc tháng 10; cặp 200.000 cùng tháng bù về 0");
    }

    [Fact]
    public void October_ShowsTheReversedOriginalAsANegativeReversalNet()
    {
        var october = CashFlowCalculator.ByDay(Everything(), null).Where(d => d.Date.Month == 10);

        var (spent, _, _, _, c) = CashFlowCalculator.Period(october);

        spent.Should().Be(1_000_000, "khoản bị đảo không phải chi tiêu");
        c.ReversalNetCents.Should().Be(-400_000);
    }

    [Fact]
    public void OneWallet_SeesItsOwnTransfers_TheFeeStaysASpend()
    {
        var vcbNovember = CashFlowCalculator.ByDay(Everything().Where(r => r.Legs.Any(l => l.AccountId == Vcb)), Vcb)
            .Where(d => d.Date.Month == 11);
        var momoNovember = CashFlowCalculator.ByDay(Everything().Where(r => r.Legs.Any(l => l.AccountId == Momo)), Momo)
            .Where(d => d.Date.Month == 11);

        var vcb = CashFlowCalculator.Period(vcbNovember);
        var momo = CashFlowCalculator.Period(momoNovember);

        vcb.CashFlow.TransferOutflowCents.Should().Be(2_000_000);
        vcb.CashFlow.TransferInflowCents.Should().Be(0);
        vcb.SpentCents.Should().Be(300_000 + 3_300 + 60_000 - 100_000, "phí chuyển ví vẫn là khoản chi thường của VCB");
        momo.CashFlow.TransferInflowCents.Should().Be(2_000_000);
        momo.Count.Should().Be(2, "MoMo là ví nguồn của khoản hoàn và lần góp quỹ; lần chuyển từ VCB sang không đếm cho MoMo, cặp đảo không đếm");
    }

    [Fact]
    public void LoanOnlyDays_AreKept_AndLoansNeverCountAsSpendOrIncome()
    {
        var days = CashFlowCalculator.ByDay([Row(Nov1, TransactionType.Debit, 5_000_000, Vcb, loan: true)], null);

        days.Should().ContainSingle();
        var (spent, income, count, _, c) = CashFlowCalculator.Period(days);
        (spent, income, count).Should().Be((0L, 0L, 0));
        c.LoanOutflowCents.Should().Be(5_000_000);
        c.NetCents.Should().Be(-5_000_000);
    }
}
