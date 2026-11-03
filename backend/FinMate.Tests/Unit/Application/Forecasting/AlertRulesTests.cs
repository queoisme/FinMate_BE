using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class QuietHoursTests
{
    private static DateTimeOffset At(int hour, int minute = 0) => new(Today.ToDateTime(new TimeOnly(hour, minute)), VietnamTime.Offset);

    [Theory]
    [InlineData(23, true)]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(12, false)]
    [InlineData(21, false)]
    public void OvernightWindow_CoversLateNightAndEarlyMorning(int hour, bool quiet)
        => new QuietHours(22 * 60, 7 * 60).Contains(At(hour)).Should().Be(quiet);

    [Fact]
    public void EndAfter_BeforeMidnight_IsTomorrowMorning()
        => new QuietHours(22 * 60, 7 * 60).EndAfter(At(23)).Should().Be(At(7).AddDays(1));

    [Fact]
    public void EndAfter_AfterMidnight_IsThisMorning()
        => new QuietHours(22 * 60, 7 * 60).EndAfter(At(3)).Should().Be(At(7));

    [Fact]
    public void SameStartAndEnd_MeansNoQuietHours()
        => new QuietHours(600, 600).Contains(At(10)).Should().BeFalse();

    [Theory]
    [InlineData("22:00", 1320)]
    [InlineData("07:30", 450)]
    [InlineData("7h", null)]
    [InlineData("25:00", null)]
    public void Parse_AcceptsOnlyHHmm(string text, int? minute) => QuietHours.Parse(text).Should().Be(minute);
}

public class LowBalanceRuleTests
{
    private static CashflowForecastDto Forecast(params (long Balance, long Flexible)[] days)
        => new(Today, Today.AddDays(days.Length - 1), 0, days[^1].Balance, 0, 0, Today, 0, "normal", "history",
            days.Select((d, i) => new CashflowDayDto(Today.AddDays(i), d.Balance, d.Balance, d.Balance, d.Flexible, [])).ToList(),
            [new CashflowCauseDto(CashflowEventKinds.RecurringOutflow, "Tiền nhà", 4_000_000)]);

    [Fact]
    public void StayingAboveThreshold_RaisesNothing()
        => LowBalanceRule.Evaluate(Forecast((900_000, 100_000), (800_000, 100_000)), 500_000).Should().BeNull();

    [Fact]
    public void DroppingBelow_GivesTheDayAndASafeDailySpend()
    {
        // Ngày 2 thấp nhất: 300k. Tới đó đã tính 3 × 100k chi linh hoạt. Giữ ≥ 500k: (300k + 300k − 500k) / 3 = 33.333đ/ngày.
        var warning = LowBalanceRule.Evaluate(Forecast((700_000, 100_000), (450_000, 100_000), (300_000, 100_000), (900_000, 100_000)), 500_000)!;

        warning.ShortfallDate.Should().Be(Today.AddDays(1));
        warning.LowestDate.Should().Be(Today.AddDays(2));
        warning.MaxDailySpendCents.Should().Be(33_333);
        warning.Causes[0].Label.Should().Be("Tiền nhà");
    }

    [Fact]
    public void NoRoomLeft_SafeDailySpendIsZero()
        => LowBalanceRule.Evaluate(Forecast((100_000, 0), (50_000, 0)), 500_000)!.MaxDailySpendCents.Should().Be(0);
}

public class BudgetRiskCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);
    private static readonly DateOnly End = new(2026, 11, 30);

    private static BudgetForecastDto Compute(long limit, long spent, DateOnly today, IReadOnlyList<ForecastTransaction> history,
        IReadOnlyList<CashflowEvent>? upcoming = null, Guid? category = null)
        => BudgetRiskCalculator.Compute(
            new BudgetRiskInput(Guid.NewGuid(), category, "Ăn uống", limit, spent, Start, End),
            history, [], upcoming ?? [], new Dictionary<Guid, Guid?>(), today, Rules, minElapsedDays: 7);

    [Fact]
    public void FastPace_IsAtRisk_WithAFairDailyAllowance()
    {
        var today = new DateOnly(2026, 11, 10);
        var history = Enumerable.Range(0, 10).Select(i => Debit(Start.AddDays(i), 200_000, "Quán")).ToList();

        var result = Compute(3_000_000, 2_000_000, today, history);

        result.Basis.Should().Be(BudgetRiskBasis.Pace);
        result.ProjectedCents.Should().Be(2_000_000 + (200_000 * 20));
        result.Status.Should().Be(BudgetRiskStatus.AtRisk);
        result.DaysRemaining.Should().Be(21);
        result.MaxDailySpendCents.Should().Be(1_000_000 / 21);
        BudgetRiskCalculator.ShouldWarn(result, 3).Should().BeTrue();
    }

    [Fact]
    public void EarlyInTheMonth_UsesHistoryInsteadOfThinPace()
    {
        var today = new DateOnly(2026, 11, 2);
        var history = Enumerable.Range(1, 60).Select(i => Debit(Start.AddDays(-i), 50_000, "Quán")).ToList();
        history.Add(Debit(Start, 900_000, "Quán"));

        var result = Compute(3_000_000, 900_000, today, history);

        result.Basis.Should().Be(BudgetRiskBasis.History);
        result.ProjectedCents.Should().Be(900_000 + (50_000 * 28));
        result.Status.Should().Be(BudgetRiskStatus.Ok);
    }

    [Fact]
    public void AlreadyOver_IsOver_AndHasNoAllowanceLeft()
    {
        var result = Compute(1_000_000, 1_200_000, new DateOnly(2026, 11, 15), []);

        result.Status.Should().Be(BudgetRiskStatus.Over);
        result.MaxDailySpendCents.Should().Be(0);
        BudgetRiskCalculator.ShouldWarn(result, 3).Should().BeFalse("vượt rồi thì cảnh báo vượt hạn mức có sẵn lo, không báo 'sắp vượt'");
    }

    [Fact]
    public void TooLateInTheMonth_DoesNotWarnEarly()
    {
        var today = new DateOnly(2026, 11, 29);
        var history = Enumerable.Range(0, 29).Select(i => Debit(Start.AddDays(i), 100_000, "Quán")).ToList();

        var result = Compute(2_950_000, 2_900_000, today, history);

        result.Status.Should().Be(BudgetRiskStatus.AtRisk);
        BudgetRiskCalculator.ShouldWarn(result, 3).Should().BeFalse();
    }

    [Fact]
    public void UpcomingBillOfTheSameCategory_CountsTowardsTheProjection()
    {
        var category = Guid.NewGuid();
        var rentId = Guid.NewGuid();
        var upcoming = new[] { new CashflowEvent(new DateOnly(2026, 11, 20), -4_000_000, "Tiền nhà", CashflowEventKinds.RecurringOutflow, rentId) };

        var result = BudgetRiskCalculator.Compute(
            new BudgetRiskInput(Guid.NewGuid(), category, "Nhà ở", 5_000_000, 0, Start, End),
            [], [], upcoming, new Dictionary<Guid, Guid?> { [rentId] = category }, new DateOnly(2026, 11, 10), Rules, 7);

        result.ProjectedCents.Should().Be(4_000_000);
    }
}

public class BillReminderRuleTests
{
    [Fact]
    public void DueWithinTheWindowAndUnpaid_IsDueSoon()
        => BillReminderRule.Evaluate([Item("Điện", 600_000, Today.AddDays(2))], [], Today, 3, Rules)
            .Should().ContainSingle(r => r.Kind == BillReminderKind.DueSoon);

    [Fact]
    public void AlreadyPaid_IsNotReminded()
        => BillReminderRule.Evaluate([Item("Điện", 600_000, Today.AddDays(2))], [Debit(Today, 600_000, "Điện")], Today, 3, Rules)
            .Should().BeEmpty();

    [Fact]
    public void OverdueBeyondGrace_AsksWhetherItIsStillUsed()
        => BillReminderRule.Evaluate([Item("Gym", 300_000, Today.AddDays(-10))], [], Today, 3, Rules)
            .Should().ContainSingle(r => r.Kind == BillReminderKind.LongOverdue && r.DaysOverdue == 10);

    [Fact]
    public void OverdueWithinGrace_StaysQuiet()
        => BillReminderRule.Evaluate([Item("Gym", 300_000, Today.AddDays(-3))], [], Today, 3, Rules).Should().BeEmpty();

    [Fact]
    public void Income_IsNeverABill()
        => BillReminderRule.Evaluate([Item("Lương", 15_000_000, Today.AddDays(1), RecurringDirection.Inflow)], [], Today, 3, Rules)
            .Should().BeEmpty();
}

public class IncomeEstimatorTests
{
    [Fact]
    public void Irregular_UsesFullMonthsOnly_AndTheTwentyFifthPercentile()
    {
        // Thu tháng 7–10 lần lượt 8, 12, 4, 10 triệu (tháng 6 bắt đầu giữa chừng, tháng 11 đang dở nên không tính).
        var history = new List<ForecastTransaction>
        {
            Credit(new DateOnly(2026, 6, 20), 9_000_000, "Khách A"),
            Credit(new DateOnly(2026, 7, 10), 8_000_000, "Khách A"),
            Credit(new DateOnly(2026, 8, 5), 12_000_000, "Khách B"),
            Credit(new DateOnly(2026, 9, 25), 4_000_000, "Khách C"),
            Credit(new DateOnly(2026, 10, 15), 10_000_000, "Khách A"),
            Credit(new DateOnly(2026, 11, 1), 1_500_000, "Khách D"),
        };

        var estimate = IncomeEstimator.Irregular(history, [], Today, 6, Rules)!;

        estimate.MonthsUsed.Should().Be(4);
        estimate.AverageMonthlyCents.Should().Be(8_500_000);
        estimate.ConservativeMonthlyCents.Should().Be(7_000_000);
        estimate.ReceivedThisMonthCents.Should().Be(1_500_000);
    }

    [Fact]
    public void Irregular_LessThanTwoMonths_IsNotEstimated()
        => IncomeEstimator.Irregular([Credit(Today.AddDays(-20), 5_000_000, "Khách")], [], Today, 6, Rules).Should().BeNull();

    [Fact]
    public void Late_AfterTheGraceDays_AndNotReceived()
    {
        var salary = Item("Lương", 15_000_000, Today.AddDays(-4), RecurringDirection.Inflow);

        IncomeEstimator.Late([salary], [], Today, 3, Rules).Should().ContainSingle(l => l.DaysLate == 4);
        IncomeEstimator.Late([salary], [Credit(Today.AddDays(-1), 15_000_000, "Lương")], Today, 3, Rules).Should().BeEmpty();
        IncomeEstimator.Late([salary], [], Today, 5, Rules).Should().BeEmpty();
    }
}
