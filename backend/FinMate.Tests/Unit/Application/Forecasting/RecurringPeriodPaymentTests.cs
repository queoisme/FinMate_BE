using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>
/// Phase 97, luật người trả + thời điểm: giao dịch cùng người trả/nhận với khoản định kỳ, trong cửa sổ ngày quanh một kỳ đến hạn, là lần trả của kỳ đó bất kể số tiền
/// và không vào thu/chi linh hoạt; ngoài cửa sổ (thưởng, dự án lẻ, mua thêm) thì vẫn là linh hoạt. Cùng một luật cho thu (K2) và chi (K4).
/// Today = thứ Hai 2026-11-02; khoản tháng ngày 5 có cửa sổ [hạn − 14, hạn + 7].
/// </summary>
public class RecurringPeriodPaymentTests
{
    private static readonly IrregularIncomeDto Monthly = new(6_000_000, 8_000_000, 6, 0);

    /// <summary>91 ngày có chi mỗi ngày, khoản thu dự án 2.800.000đ mỗi 14 ngày (không thuộc khoản định kỳ nào).</summary>
    private static List<ForecastTransaction> Freelancer()
    {
        var history = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), 50_000, "Quán ăn")).ToList();
        history.AddRange(Enumerable.Range(1, 91).Where(i => i % 14 == 0).Select(i => Credit(Today.AddDays(-i), 2_800_000, "Khách hàng A")));
        return history;
    }

    private static IEnumerable<ForecastTransaction> OnDay5(TransactionType type, long amount, string name)
        => new[] { new DateOnly(2026, 8, 5), new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 5) }
            .Select(d => type == TransactionType.Credit ? Credit(d, amount, name) : Debit(d, amount, name));

    private static long[] Income(List<ForecastTransaction> history, params RecurringItemView[] items)
        => IrregularIncomeBaselineCalculator.Compute(history, items, Today, Rules, Monthly).Expected;

    [Fact]
    public void A_SalaryRaisedBy30Percent_ItsOldPriceCreditsAreNotIrregularIncome()
    {
        var history = Freelancer();
        history.AddRange(OnDay5(TransactionType.Credit, 15_000_000, "Lương công ty"));
        var salary = Item("Lương công ty", 19_500_000, new DateOnly(2026, 11, 5), RecurringDirection.Inflow);

        Income(history, salary).Should().Equal(Income(Freelancer()), "các lần nhận giá cũ là lương của kỳ đó, kỳ kế tiếp đã được chiếu riêng");
    }

    [Fact]
    public void B_ABonusFromTheSameCompanyOnAnotherDay_IsStillIrregularIncome()
    {
        var bonuses = new[]
        {
            Credit(new DateOnly(2026, 10, 17), 5_000_000, "Lương công ty"),   // ngoài mọi cửa sổ (5/10 tới 12/10, 22/10 tới 12/11)
            Credit(new DateOnly(2026, 9, 28), 3_000_000, "Lương công ty"),    // trong cửa sổ kỳ 5/10, nhưng kỳ đó đã có lần nhận lương đúng ngày
        };
        var history = Freelancer();
        history.AddRange(OnDay5(TransactionType.Credit, 15_000_000, "Lương công ty"));
        history.AddRange(bonuses);
        var salary = Item("Lương công ty", 19_500_000, new DateOnly(2026, 11, 5), RecurringDirection.Inflow);

        Income(history, salary).Should().Equal(Income([.. Freelancer(), .. bonuses]));
        Income(history, salary).Sum().Should().BeGreaterThan(Income(Freelancer()).Sum());
    }

    [Fact]
    public void C_ABillRaisedBy25Percent_IsNotCountedTwiceInFlexibleSpending()
    {
        var spending = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), 100_000, "Quán ăn")).ToList();
        var history = spending.Concat(OnDay5(TransactionType.Debit, 1_000_000, "Tiền điện")).ToList();
        var bill = Item("Tiền điện", 1_250_000, new DateOnly(2026, 11, 5));

        SpendingBaselineCalculator.Compute(history, [bill], 0, Today, Rules).Expected
            .Should().Equal(SpendingBaselineCalculator.Compute(spending, [], 0, Today, Rules).Expected);
    }

    [Fact]
    public void D_AClientsAdHocProjectBesideItsRetainer_IsStillIrregularIncome()
    {
        // Phí định kỳ 5 triệu ngày 20; dự án lẻ 8 triệu ngày 3/10 (ngoài cửa sổ) và 14/10 (trong cửa sổ kỳ 20/10, nhưng phí kỳ đó đã về đúng số tiền).
        var retainer = new[] { new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 20) }.Select(d => Credit(d, 5_000_000, "Khách hàng B"));
        var projects = new[] { Credit(new DateOnly(2026, 10, 3), 8_000_000, "Khách hàng B"), Credit(new DateOnly(2026, 10, 14), 8_000_000, "Khách hàng B") };
        var history = Freelancer().Concat(retainer).Concat(projects).ToList();
        var item = Item("Khách hàng B", 5_000_000, new DateOnly(2026, 11, 20), RecurringDirection.Inflow);

        Income(history, item).Should().Equal(Income([.. Freelancer(), .. projects]));
    }

    [Fact]
    public void EachPeriodTakesOnePayment_PreferringTheAmountWithinTolerance_ThenTheDayClosestToTheDueDate()
    {
        var item = Item("Lương công ty", 19_500_000, new DateOnly(2026, 11, 5), RecurringDirection.Inflow);
        var early = Credit(new DateOnly(2026, 9, 25), 4_000_000, "Lương công ty");
        var oldPrice = Credit(new DateOnly(2026, 10, 6), 15_000_000, "Lương công ty");
        var newPrice = Credit(new DateOnly(2026, 9, 4), 19_000_000, "Lương công ty");
        var other = Credit(new DateOnly(2026, 9, 1), 19_500_000, "Khách hàng A");

        var ids = RecurringMatcher.PeriodPayments([early, oldPrice, newPrice, other], [item], Rules);

        ids.Should().BeEquivalentTo([oldPrice.Id, newPrice.Id], "kỳ 5/10: 6/10 gần hạn hơn 25/9; kỳ 5/9: đúng số tiền; người trả khác không bao giờ khớp");
    }

    [Fact]
    public void APaymentBeforeTheTrackerMovedTheDueDate_CountsForTheNextDueDate()
    {
        // Engine coi khoản này là đã trả cho kỳ NextDueDate (FindPayment), nên nó cũng không được vào thói quen.
        var item = Item("Tiền điện", 1_250_000, Today.AddDays(2));
        var paid = Debit(Today.AddDays(-1), 1_600_000, "Tiền điện");

        RecurringMatcher.PeriodPayments([paid], [item], Rules).Should().ContainSingle().Which.Should().Be(paid.Id);
        RecurringPaymentFilter.For([paid], [item], Rules).IsPayment(paid).Should().BeTrue();
        RecurringMatcher.MatchesAny(paid, [item], Rules).Should().BeFalse("ngoài dung sai 10%: trước Phase 97 nó lọt vào chi linh hoạt");
    }

    [Fact]
    public void AfterASkippedPeriod_TheCombinedPaymentOfTheNextPeriodIsAPayment_NotFlexibleSpending()
    {
        // K3: bỏ kỳ 5/9, kỳ 5/10 trả gộp hai kỳ. Tracker dừng ở kỳ chưa trả (NextDueDate = 5/9), nên lần trả 5/10 nằm SAU NextDueDate;
        // engine coi kỳ 5/10 đã trả, nên 2 triệu đó không được vào chi linh hoạt.
        var spending = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), 100_000, "Quán ăn")).ToList();
        var combined = Debit(new DateOnly(2026, 10, 5), 2_000_000, "Tiền điện");
        var history = spending.Append(Debit(new DateOnly(2026, 8, 5), 1_000_000, "Tiền điện")).Append(combined).ToList();
        var bill = Item("Tiền điện", 1_000_000, new DateOnly(2026, 9, 5));

        RecurringMatcher.PeriodPayments(history, [bill], Rules).Should().Contain(combined.Id);
        SpendingBaselineCalculator.Compute(history, [bill], 0, Today, Rules).Expected
            .Should().Equal(SpendingBaselineCalculator.Compute(spending, [], 0, Today, Rules).Expected);
    }

    [Fact]
    public void AnInstallmentThatHasRunOut_DoesNotClaimLaterTransactionsFromTheSamePayee()
    {
        // Khoản trả góp kỳ cuối 5/9; mua thêm của cùng cửa hàng ngày 5/10 là chi linh hoạt, không phải kỳ thứ n+1.
        var installment = Item("Cửa hàng điện máy", 1_000_000, new DateOnly(2026, 9, 5)) with { RemainingOccurrences = 1 };
        var later = Debit(new DateOnly(2026, 10, 5), 3_000_000, "Cửa hàng điện máy");
        var ended = installment with { EndDate = new DateOnly(2026, 9, 5), RemainingOccurrences = null };

        RecurringMatcher.PeriodPayments([later], [installment], Rules).Should().BeEmpty();
        RecurringMatcher.PeriodPayments([later], [ended], Rules).Should().BeEmpty();
    }
}
