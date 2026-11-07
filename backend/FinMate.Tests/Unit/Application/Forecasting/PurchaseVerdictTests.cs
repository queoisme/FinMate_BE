using FinMate.Application.Forecasting;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Các tình huống của đặc tả đánh giá khoản mua, kiểm bằng số cụ thể vì bộ tính là hàm thuần.</summary>
public class PurchaseVerdictTests
{
    // Ví dụ §6.4: ngày 10 có 5.000.000đ, ngày 12 trả nhà 3.000.000đ, giữ 1.000.000đ sinh hoạt tới ngày 15, ngày 15 nhận lương.
    private static readonly DateOnly D10 = new(2026, 11, 10);
    private static readonly DateOnly Payday = new(2026, 11, 15);
    private static readonly DateOnly NextPayday = new(2026, 12, 15);

    private static readonly PurchaseQuality Good = new(true, false, true, false, 1, 7);
    private static readonly PurchaseBudgetImpactDto NoBudget = new(PurchaseImpactStatuses.Unknown, null, null, null, null);
    private static readonly PurchasePlanImpactDto NoPlan = new(PurchaseImpactStatuses.Unknown, null, null);

    /// <summary>Số dư nếu không mua: thấp nhất 1.000.000đ ngày 14 (trước lương), sau lương cao; kéo dài tới 15/12.</summary>
    private static long BeforeOn(DateOnly day)
    {
        if (day < Payday)
        {
            return day.Day switch { 10 => 4_800_000, 11 => 4_600_000, 12 => 1_400_000, 13 => 1_300_000, _ => 1_000_000 };
        }

        return 10_900_000 - ((day.DayNumber - Payday.DayNumber) * 100_000);
    }

    private static IReadOnlyList<PurchaseDay> Days(DateOnly purchase, long amount, DateOnly last)
    {
        var days = new List<PurchaseDay>();
        for (var d = D10; d <= last; d = d.AddDays(1))
        {
            var before = BeforeOn(d);
            var after = d >= purchase ? before - amount : before;
            days.Add(new PurchaseDay(d, before, after, after - 100_000));
        }

        return days;
    }

    private static PurchaseVerdictInput Input(
        DateOnly purchase, long amount, long buffer = 0, PurchaseQuality? quality = null,
        PurchaseBudgetImpactDto? budget = null, PurchasePlanImpactDto? plan = null)
    {
        var days = Days(purchase, amount, NextPayday.AddDays(-1));
        DateOnly[] incomes = [Payday, NextPayday];
        var period = PurchaseVerdict.ResolvePeriod(D10, purchase, incomes, null, 14, days[^1].Date);
        return new PurchaseVerdictInput(D10, purchase, amount, buffer, period, days, incomes, 14, quality ?? Good, budget ?? NoBudget, plan ?? NoPlan);
    }

    [Fact]
    public void BillBeforePayday_MakesAPurchaseTodayShortOfMoney()
    {
        var result = PurchaseVerdict.Evaluate(Input(D10, 1_500_000));

        result.Verdict.Should().Be(PurchaseVerdicts.ShortfallRisk);
        result.LowestBefore.Should().Be(new PurchasePointDto(new DateOnly(2026, 11, 14), 1_000_000));
        result.LowestAfter.Should().Be(new PurchasePointDto(new DateOnly(2026, 11, 14), -500_000));
        result.ShortfallCents.Should().Be(500_000);
    }

    [Fact]
    public void ShortfallResult_OffersTheMostAffordablePriceAndTheFirstSafeDay()
    {
        var result = PurchaseVerdict.Evaluate(Input(D10, 1_500_000));

        result.MaxAffordableCents.Should().Be(1_000_000);
        result.EarliestSafeDate.Should().Be(Payday);
    }

    [Fact]
    public void BuyingAfterPayday_GivesADifferentAnswerEvenThoughTheMonthTotalIsTheSame()
    {
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 1_500_000));

        result.Verdict.Should().Be(PurchaseVerdicts.FitsPlan);
        result.Status.Should().Be(PurchaseStatuses.Assessable);
        result.LowestAfter!.BalanceCents.Should().BeGreaterThan(0);
        result.ShortfallCents.Should().Be(0);
    }

    [Fact]
    public void LowestPointStillAboveZeroButBelowTheUsersBuffer_IsWorthConsidering()
    {
        // §6.6: thấp nhất còn 300.000đ nhưng vùng đệm người dùng đặt là 500.000đ.
        var result = PurchaseVerdict.Evaluate(Input(D10, 700_000, buffer: 500_000));

        result.LowestAfter!.BalanceCents.Should().Be(300_000);
        result.Verdict.Should().Be(PurchaseVerdicts.Consider);
    }

    [Fact]
    public void LowestPointExactlyAtTheBuffer_Fits()
    {
        var result = PurchaseVerdict.Evaluate(Input(D10, 500_000, buffer: 500_000));

        result.Verdict.Should().Be(PurchaseVerdicts.FitsPlan);
    }

    [Fact]
    public void WithoutAKnownNextIncome_TheResultIsOnlyPartial_AndNeverSaysItFits()
    {
        var quality = Good with { IncomeScheduleKnown = false };
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 100_000, quality: quality));

        result.Status.Should().Be(PurchaseStatuses.Partial);
        result.Verdict.Should().Be(PurchaseVerdicts.InsufficientData);
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.NoIncomeSchedule);
    }

    [Fact]
    public void WithoutAKnownNextIncome_AUserChosenHorizonIsAcceptedAsTheBasis()
    {
        var quality = Good with { IncomeScheduleKnown = false, HorizonChosenByUser = true };
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 100_000, quality: quality));

        result.Status.Should().Be(PurchaseStatuses.Assessable);
        result.Verdict.Should().Be(PurchaseVerdicts.FitsPlan);
    }

    [Fact]
    public void PartialData_StillReportsARealShortfall()
    {
        var quality = Good with { LowConfidence = true };
        var result = PurchaseVerdict.Evaluate(Input(D10, 1_500_000, quality: quality));

        result.Status.Should().Be(PurchaseStatuses.Partial);
        result.Verdict.Should().Be(PurchaseVerdicts.ShortfallRisk);
    }

    [Fact]
    public void StaleBalance_MakesTheResultPartial()
    {
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 100_000, quality: Good with { BalanceAgeDays = 12 }));

        result.Status.Should().Be(PurchaseStatuses.Partial);
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.StaleBalance);
    }

    [Fact]
    public void NoAccountInTheForecast_IsInsufficientData_WithoutMadeUpNumbers()
    {
        var result = PurchaseVerdict.Evaluate(Input(D10, 100_000, quality: Good with { HasAccount = false }));

        result.Status.Should().Be(PurchaseStatuses.InsufficientData);
        result.Verdict.Should().Be(PurchaseVerdicts.InsufficientData);
        result.LowestAfter.Should().BeNull();
        result.MaxAffordableCents.Should().BeNull();
    }

    [Fact]
    public void EnoughCash_ButOverTheCategoryBudget_IsOffPlan_NotShortOfMoney()
    {
        var budget = new PurchaseBudgetImpactDto(PurchaseImpactStatuses.Over, "Mua sắm", 2_000_000, 1_800_000, 2_300_000);
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 500_000, budget: budget));

        result.Verdict.Should().Be(PurchaseVerdicts.OffPlan);
        result.ShortfallCents.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.BudgetOver);
    }

    [Fact]
    public void BudgetOrPlanUnknown_IsNotTreatedAsZero()
    {
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 500_000));

        result.Verdict.Should().Be(PurchaseVerdicts.FitsPlan);
        result.Warnings.Select(w => w.Code).Should().Contain(new[] { PurchaseWarningCodes.BudgetUnknown, PurchaseWarningCodes.PlanUnknown });
    }

    [Fact]
    public void MonthlyPlanGoingNegative_IsOffPlan()
    {
        var plan = new PurchasePlanImpactDto(PurchaseImpactStatuses.Over, 300_000, -200_000);
        var result = PurchaseVerdict.Evaluate(Input(new DateOnly(2026, 11, 16), 500_000, plan: plan));

        result.Verdict.Should().Be(PurchaseVerdicts.OffPlan);
    }

    [Fact]
    public void AlreadyShortWithoutThePurchase_IsSaidSo()
    {
        var days = new List<PurchaseDay> { new(D10, -200_000, -300_000, -400_000), new(D10.AddDays(1), 5_000_000, 4_900_000, 4_800_000) };
        var period = new PurchasePeriodDto(D10, D10.AddDays(1), PurchasePeriodBases.NextIncome);
        var input = new PurchaseVerdictInput(D10, D10, 100_000, 0, period, days, [D10.AddDays(2)], 14, Good, NoBudget, NoPlan);

        var result = PurchaseVerdict.Evaluate(input);

        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.AlreadyShort);
        result.Verdict.Should().Be(PurchaseVerdicts.ShortfallRisk);
    }

    [Fact]
    public void ConservativeLineBelowZero_IsOnlyAWarning_NotAVerdict()
    {
        var days = new List<PurchaseDay> { new(D10, 1_000_000, 500_000, -50_000), new(D10.AddDays(1), 1_000_000, 500_000, -50_000) };
        var period = new PurchasePeriodDto(D10, D10.AddDays(1), PurchasePeriodBases.NextIncome);
        var input = new PurchaseVerdictInput(D10, D10, 500_000, 0, period, days, [D10.AddDays(2)], 14, Good, NoBudget, NoPlan);

        var result = PurchaseVerdict.Evaluate(input);

        result.Verdict.Should().Be(PurchaseVerdicts.FitsPlan);
        result.Warnings.Should().Contain(w => w.Code == PurchaseWarningCodes.LowMargin);
    }

    [Fact]
    public void LargeAmounts_StayExactInLongArithmetic()
    {
        var days = new List<PurchaseDay> { new(D10, 100_000_000_000, 1, 1) };
        var period = new PurchasePeriodDto(D10, D10, PurchasePeriodBases.UserChosen);
        var quality = Good with { IncomeScheduleKnown = false, HorizonChosenByUser = true };
        var input = new PurchaseVerdictInput(D10, D10, 99_999_999_999, 0, period, days, [], 14, quality, NoBudget, NoPlan);

        var result = PurchaseVerdict.Evaluate(input);

        result.LowestAfter!.BalanceCents.Should().Be(1);
        result.MaxAffordableCents.Should().Be(100_000_000_000);
    }

    [Fact]
    public void Period_EndsTheDayBeforeTheNextIncomeAfterThePurchaseDay()
    {
        DateOnly[] incomes = [Payday, NextPayday];

        PurchaseVerdict.ResolvePeriod(D10, D10, incomes, null, 14, NextPayday).Should()
            .Be(new PurchasePeriodDto(D10, Payday.AddDays(-1), PurchasePeriodBases.NextIncome));
        PurchaseVerdict.ResolvePeriod(D10, Payday, incomes, null, 14, NextPayday).Should()
            .Be(new PurchasePeriodDto(D10, NextPayday.AddDays(-1), PurchasePeriodBases.NextIncome), "mua đúng ngày lương thì kỳ tính tới lương kế tiếp");
    }

    [Fact]
    public void Period_WithoutKnownIncome_UsesTheChosenHorizon_CappedByTheForecastWindow()
    {
        var chosen = PurchaseVerdict.ResolvePeriod(D10, D10, [], 7, 14, D10.AddDays(30));
        var fallback = PurchaseVerdict.ResolvePeriod(D10, D10, [], null, 14, D10.AddDays(30));
        var capped = PurchaseVerdict.ResolvePeriod(D10, D10, [], 30, 14, D10.AddDays(20));

        chosen.Should().Be(new PurchasePeriodDto(D10, D10.AddDays(7), PurchasePeriodBases.UserChosen));
        fallback.Should().Be(new PurchasePeriodDto(D10, D10.AddDays(14), PurchasePeriodBases.DefaultHorizon));
        capped.To.Should().Be(D10.AddDays(20));
    }

    [Theory]
    [InlineData(1_500_000, PurchaseVerdicts.ShortfallRisk)]
    [InlineData(100_000, PurchaseVerdicts.FitsPlan)]
    public void Summary_UsesTheComputedNumbers_AndNeverPromisesAnything(long amount, string verdict)
    {
        var input = Input(D10, amount);
        var result = PurchaseVerdict.Evaluate(input);

        var text = PurchaseVerdict.Summary(input, result, "quần");

        result.Verdict.Should().Be(verdict);
        text.Should().Contain("quần").And.Contain(Vnd(amount));
        text.Should().NotContainAny("chắc chắn", "nên mua");
    }

    [Fact]
    public void Summary_WhenNothingCouldBeComputed_AsksForData()
    {
        var input = Input(D10, 100_000, quality: Good with { HasAccount = false });

        PurchaseVerdict.Summary(input, PurchaseVerdict.Evaluate(input), null).Should().Contain("Chưa đủ dữ liệu");
    }

    private static string Vnd(long amount) => amount.ToString("N0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." }) + "đ";
}
