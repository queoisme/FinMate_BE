using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>
/// Phase 92: khoản có kỳ cuối chỉ được chiếu đúng số kỳ còn lại (BR-INS-07) hoặc tới ngày kết thúc. S1: kỳ quá hạn của khoản có tổng số kỳ
/// không tiêu kỳ, chiếu vào ngày mai, và không vào luồng tạm dừng "còn dùng không?". Khoản không có kỳ cuối chạy như cũ (BR-INS-02).
/// </summary>
public class InstallmentProjectionTests
{
    private static readonly DateOnly SixMonths = Today.AddMonths(6);

    private static RecurringItemView Installment(DateOnly nextDue, int? remaining = null, DateOnly? endDate = null,
        RecurringDirection direction = RecurringDirection.Outflow, long amount = 1_200_000)
        => Item("FE Credit", amount, nextDue, direction) with { RemainingOccurrences = remaining, EndDate = endDate };

    private static IReadOnlyList<DateOnly> Dates(IEnumerable<CashflowEvent> events) => events.Select(e => e.Date).OrderBy(d => d).ToList();

    [Fact]
    public void Cashflow_ProjectsOnlyTheRemainingOccurrences()
    {
        var events = CashflowEngine.RecurringEvents([Installment(new DateOnly(2026, 11, 15), remaining: 3)], [], Today, SixMonths, Rules);

        Dates(events).Should().Equal(new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 15));
        events.Should().OnlyContain(e => e.AmountCents == -1_200_000);
    }

    [Fact]
    public void Cashflow_StopsAtTheEndDate()
        => Dates(CashflowEngine.RecurringEvents(
                [Installment(new DateOnly(2026, 11, 15), endDate: new DateOnly(2027, 1, 20))], [], Today, SixMonths, Rules))
            .Should().Equal(new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 15));

    [Fact]
    public void Cashflow_WithoutAnEnd_IsUnchanged_SixTimesInSixMonths()
        => CashflowEngine.RecurringEvents([Item("Netflix", 260_000, new DateOnly(2026, 11, 15))], [], Today, SixMonths, Rules)
            .Should().HaveCount(6);

    [Fact]
    public void Cashflow_NothingLeft_ProjectsNothing()
        => CashflowEngine.RecurringEvents([Installment(new DateOnly(2026, 11, 15), remaining: 0)], [], Today, SixMonths, Rules).Should().BeEmpty();

    [Fact]
    public void Cashflow_ADueDateAlreadySeenPaid_StillUsesUpAnOccurrence()
    {
        var item = Installment(Today.AddDays(1), remaining: 2);

        var events = CashflowEngine.RecurringEvents([item], [Debit(Today, 1_200_000, "FE Credit")], Today, SixMonths, Rules);

        Dates(events).Should().Equal(Today.AddDays(1).AddMonths(1));
    }

    [Fact]
    public void S1_AnOverdueInstallment_IsNotLost_ItLandsTomorrow_AndTheRestKeepTheirAnchorDates()
    {
        var item = Installment(Today.AddDays(-20), remaining: 3);   // 13/10, quá hạn 20 ngày, quá cả ân hạn 7 ngày

        var events = CashflowEngine.RecurringEvents([item], [], Today, SixMonths, Rules);

        Dates(events).Should().Equal(Today.AddDays(1), new DateOnly(2026, 11, 13), new DateOnly(2026, 12, 13));
        events.Should().OnlyContain(e => e.AmountCents == -1_200_000);
    }

    [Fact]
    public void S1_OverdueWithinGrace_AlsoLandsTomorrow()
        => Dates(CashflowEngine.RecurringEvents([Installment(Today.AddDays(-3), remaining: 2)], [], Today, SixMonths, Rules))
            .Should().Equal(Today.AddDays(1), Today.AddDays(-3).AddMonths(1));

    [Fact]
    public void S1_OnlyAppliesToTotalOccurrences_AnEndDateItemKeepsTheOldOverdueRule()
        => CashflowEngine.RecurringEvents(
                [Installment(Today.AddDays(-20), endDate: Today.AddDays(5))], [], Today, Today.AddDays(5), Rules)
            .Should().BeEmpty("quá ân hạn thì bỏ như cũ, kỳ sau đã qua ngày kết thúc");

    [Fact]
    public void Income_WithATotal_OverdueIsStillNotCounted_AndUsesUpItsOccurrence()
        => Dates(CashflowEngine.RecurringEvents(
                [Installment(Today.AddDays(-2), remaining: 3, direction: RecurringDirection.Inflow)], [], Today, SixMonths, Rules))
            .Should().Equal(Today.AddDays(-2).AddMonths(1), Today.AddDays(-2).AddMonths(2));

    [Fact]
    public void Income_StopsAtItsLastOccurrence()
        => CashflowEngine.RecurringEvents(
                [Installment(new DateOnly(2026, 11, 10), remaining: 2, direction: RecurringDirection.Inflow, amount: 3_000_000)], [], Today, SixMonths, Rules)
            .Should().HaveCount(2).And.OnlyContain(e => e.AmountCents == 3_000_000);

    [Fact]
    public void Occurrences_AreCappedToo()
        => RecurringSchedule.Occurrences(Installment(new DateOnly(2026, 11, 15), remaining: 2), Today, SixMonths)
            .Should().Equal(new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 15));

    [Fact]
    public void Occurrences_CountFromTheNextDueDate_NotFromTheRangeStart()
        => RecurringSchedule.Occurrences(Installment(new DateOnly(2026, 10, 15), remaining: 2), Today, SixMonths)
            .Should().Equal(new DateOnly(2026, 11, 15));

    [Fact]
    public void BillReminder_InstallmentLongOverdue_IsAnOverdueNotice_NotAPause()
    {
        var reminder = BillReminderRule.Evaluate([Installment(Today.AddDays(-20), remaining: 3)], [], Today, 3, Rules).Should().ContainSingle().Subject;

        reminder.Kind.Should().Be(BillReminderKind.Overdue);
        reminder.DaysOverdue.Should().Be(20);
        reminder.DueDate.Should().Be(Today.AddDays(-20));
    }

    [Fact]
    public void BillReminder_InstallmentOneDayLate_IsAlreadyAnOverdueNotice()
        => BillReminderRule.Evaluate([Installment(Today.AddDays(-1), remaining: 3)], [], Today, 3, Rules)
            .Should().ContainSingle().Which.Kind.Should().Be(BillReminderKind.Overdue);

    [Fact]
    public void BillReminder_OpenEndedBill_StillGetsPausedAfterGrace()
        => BillReminderRule.Evaluate([Item("Gym", 300_000, Today.AddDays(-10))], [], Today, 3, Rules)
            .Should().ContainSingle().Which.Kind.Should().Be(BillReminderKind.LongOverdue);

    [Fact]
    public void BillReminder_SkipsFinishedItems()
    {
        BillReminderRule.Evaluate([Installment(Today.AddDays(2), remaining: 0)], [], Today, 3, Rules).Should().BeEmpty();
        BillReminderRule.Evaluate([Installment(Today.AddDays(2), endDate: Today.AddDays(1))], [], Today, 3, Rules).Should().BeEmpty();
    }

    [Fact]
    public void LateIncome_SkipsFinishedItems()
        => IncomeEstimator.Late([Installment(Today.AddDays(-10), remaining: 0, direction: RecurringDirection.Inflow)], [], Today, 3, Rules)
            .Should().BeEmpty();

    [Fact]
    public void SpendablePeriod_IgnoresAnIncomeThatHasEnded()
    {
        var finished = Installment(Today.AddDays(5), remaining: 0, direction: RecurringDirection.Inflow);

        RecurringSchedule.Occurrences(finished, Today.AddDays(1), Today.AddDays(40)).Should().BeEmpty();
    }
}
