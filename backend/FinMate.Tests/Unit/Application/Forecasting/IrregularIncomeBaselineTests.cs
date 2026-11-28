using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 97, Task 3: thu không định kỳ thành mức thu theo ngày cho đường số dư (D2, D3, D4).</summary>
public class IrregularIncomeBaselineTests
{
    private static readonly IrregularIncomeDto Monthly = new(ConservativeMonthlyCents: 6_000_000, AverageMonthlyCents: 8_000_000, MonthsUsed: 6, ReceivedThisMonthCents: 0);

    /// <summary>91 ngày có chi mỗi ngày (đủ lịch sử), khoản thu dự án 2.800.000đ mỗi 14 ngày: trung vị theo thứ = 0, trung bình thì không.</summary>
    private static List<ForecastTransaction> Freelancer()
    {
        var history = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.AddRange(Enumerable.Range(1, 91).Where(i => i % 14 == 0).Select(i => Credit(Today.AddDays(-i), 2_800_000, "Khách hàng A")));
        return history;
    }

    [Fact]
    public void LumpyIncome_IsProjectedAsTheWeekdayMean_EvenThoughItsMedianIsZero()
    {
        var baseline = IrregularIncomeBaselineCalculator.Compute(Freelancer(), [], Today, Rules, Monthly);

        baseline.Basis.Should().Be(SpendingBaseline.BasisHistory);
        var total = Enumerable.Range(0, 7).Sum(d => baseline.Expected[d]);
        total.Should().BeGreaterThan(0, "thu dồn vào vài ngày: trung vị theo thứ là 0 nhưng kỳ vọng thì không");
        var weekdayOfPayments = (int)Today.AddDays(-14).DayOfWeek;
        baseline.Expected[weekdayOfPayments].Should().Be((long)Math.Round(2_800_000 * 6 / 13.0, MidpointRounding.AwayFromZero), "6 khoản thu trên 13 ngày cùng thứ trong 91 ngày");
    }

    [Fact]
    public void CreditsOfATrackedRecurringIncome_AreNotCountedTwice()
    {
        var history = Freelancer();
        history.AddRange(Enumerable.Range(1, 3).Select(m => Credit(Today.AddDays(-30 * m), 15_000_000, "Lương công ty")));
        var salary = Item("Lương công ty", 15_000_000, Today.AddDays(5), RecurringDirection.Inflow);

        var withItem = IrregularIncomeBaselineCalculator.Compute(history, [salary], Today, Rules, Monthly);
        var freelanceOnly = IrregularIncomeBaselineCalculator.Compute(Freelancer(), [], Today, Rules, Monthly);

        withItem.Expected.Should().Equal(freelanceOnly.Expected, "lương đã được engine đặt riêng vào ngày đến hạn");
    }

    [Fact]
    public void ExcludedRefundAndLoanCredits_AndDebits_AreIgnored()
    {
        var history = Freelancer();
        history.Add(Credit(Today.AddDays(-3), 9_000_000, "Bán xe") with { ExcludedFromForecast = true });
        history.Add(Credit(Today.AddDays(-4), 9_000_000, "Shop") with { Relation = TransactionRelation.Refund, RelatedTransactionId = Guid.NewGuid() });
        history.Add(Credit(Today.AddDays(-5), 9_000_000, "Người thân") with { IsLoan = true });

        IrregularIncomeBaselineCalculator.Compute(history, [], Today, Rules, Monthly).Expected
            .Should().Equal(IrregularIncomeBaselineCalculator.Compute(Freelancer(), [], Today, Rules, Monthly).Expected);
    }

    [Fact]
    public void Conservative_IsScaledByTheMonthlyP25OverMean_AndOptimisticIsTheExpectedLine()
    {
        var baseline = IrregularIncomeBaselineCalculator.Compute(Freelancer(), [], Today, Rules, Monthly);

        for (var d = 0; d < 7; d++)
        {
            baseline.Conservative[d].Should().Be((long)Math.Round(baseline.Expected[d] * 0.75, MidpointRounding.AwayFromZero));
            baseline.Optimistic[d].Should().Be(baseline.Expected[d]);
        }
    }

    [Fact]
    public void WithoutTwoFullMonths_TheConservativeLineGetsNoIrregularIncome()
    {
        var baseline = IrregularIncomeBaselineCalculator.Compute(Freelancer(), [], Today, Rules, monthly: null);

        baseline.Conservative.Should().OnlyContain(v => v == 0);
        baseline.Expected.Sum().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ThinHistory_UsesAFlatAverage_WithNothingOnTheConservativeLine()
    {
        var history = Enumerable.Range(1, 10).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.Add(Credit(Today.AddDays(-2), 1_000_000, "Khách hàng A"));

        var baseline = IrregularIncomeBaselineCalculator.Compute(history, [], Today, Rules, Monthly);

        baseline.Basis.Should().Be(SpendingBaseline.BasisAverage);
        baseline.Expected.Should().OnlyContain(v => v == 100_000, "1.000.000đ trên 10 ngày đã có");
        baseline.Optimistic.Should().Equal(baseline.Expected);
        baseline.Conservative.Should().OnlyContain(v => v == 0);
    }

    [Fact]
    public void NoHistory_ProjectsNothing()
    {
        var baseline = IrregularIncomeBaselineCalculator.Compute([], [], Today, Rules, Monthly);

        baseline.Basis.Should().Be(SpendingBaseline.BasisNone);
        baseline.Expected.Should().OnlyContain(v => v == 0);
    }
}
