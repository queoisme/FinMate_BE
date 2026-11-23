using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.FinanceMethods;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Sổ vay trong dự báo (Phase 90, chép từ bản sổ nợ Phase 86 cũ): dải lạc quan của engine, BR-DEBT-15, 16, 17, D11, D12, D13.</summary>
public class LoanForecastTests
{
    private static Loan Debt(LoanDirection direction, long outstanding, DateOnly? due, string name = "Hoa", LoanStatus status = LoanStatus.Open) => new()
    {
        Id = Guid.NewGuid(), Title = $"Khoản {name}", Counterparty = name, Direction = direction, Status = status, PrincipalCents = outstanding, RepaidCents = 0, DueDate = due,
    };

    private static SpendingBaseline Flat(long perDay)
    {
        var v = Enumerable.Repeat(perDay, 7).ToArray();
        return new SpendingBaseline(v, v.ToArray(), v.ToArray(), ForecastConfidence.Normal, 60, SpendingBaseline.BasisHistory);
    }

    private static CashflowForecastDto Project(IReadOnlyList<CashflowEvent> events)
        => CashflowEngine.Project(new CashflowInput(Today, Today.AddDays(29), 10_000_000, Flat(0), 0, events, []));

    [Fact]
    public void AnOptimisticOnlyEvent_MovesOnlyTheHighLine()
    {
        var result = Project([new CashflowEvent(Today.AddDays(3), 1_000_000, "Lan", CashflowEventKinds.LoanInflow, null, CashflowBand.OptimisticOnly)]);

        var day = result.Days.Single(d => d.Date == Today.AddDays(3));
        day.ExpectedBalanceCents.Should().Be(10_000_000);
        day.LowBalanceCents.Should().Be(10_000_000);
        day.HighBalanceCents.Should().Be(11_000_000);
        day.Events.Single().OptimisticOnly.Should().BeTrue();
        result.EndBalanceCents.Should().Be(10_000_000);
        result.EndHighBalanceCents.Should().Be(11_000_000);
    }

    [Fact]
    public void ABorrowedDebtDueInThePeriod_LeavesAllThreeLines_OnItsDueDate_AsAUserEnteredCause()
    {
        var hoa = Debt(LoanDirection.Borrow, 3_000_000, Today.AddDays(23));

        var events = LoanForecast.Events([hoa], Today, Today.AddDays(29));
        var result = Project(events);

        events.Should().ContainSingle(e => e.Date == Today.AddDays(23) && e.AmountCents == -3_000_000 && e.Band == CashflowBand.All && e.LoanId == hoa.Id);
        var day = result.Days.Single(d => d.Date == Today.AddDays(23));
        day.ExpectedBalanceCents.Should().Be(7_000_000);
        day.LowBalanceCents.Should().Be(7_000_000);
        day.HighBalanceCents.Should().Be(7_000_000);
        result.MainCauses.Should().ContainSingle(c => c.Kind == CashflowEventKinds.LoanOutflow && c.Source == CashflowSources.UserEntered && c.AmountCents == 3_000_000);
    }

    [Fact]
    public void AnOverdueBorrowedDebt_CountsTomorrow_OrOnTheLastDayIfThePeriodEndsToday()
    {
        var overdue = Debt(LoanDirection.Borrow, 2_000_000, Today.AddDays(-5));

        LoanForecast.Events([overdue], Today, Today.AddDays(10)).Single().Date.Should().Be(Today.AddDays(1));
        LoanForecast.Events([overdue], Today, Today).Single().Date.Should().Be(Today);
    }

    [Fact]
    public void ALentDebt_IsOptimisticOnly_AndDropsOnceOverdue()
    {
        var lan = Debt(LoanDirection.Lend, 1_000_000, Today.AddDays(18), "Lan");
        var late = Debt(LoanDirection.Lend, 1_000_000, Today.AddDays(-1), "Lan");

        LoanForecast.Events([lan], Today, Today.AddDays(29)).Single().Band.Should().Be(CashflowBand.OptimisticOnly);
        LoanForecast.Events([late], Today, Today.AddDays(29)).Should().BeEmpty();
    }

    [Fact]
    public void UndatedClosedOrBeyondThePeriod_StayOut_AndUndatedAreCounted()
    {
        var undatedBorrowed = Debt(LoanDirection.Borrow, 700_000, null);
        var undatedLent = Debt(LoanDirection.Lend, 300_000, null);
        var settled = Debt(LoanDirection.Borrow, 500_000, Today.AddDays(2), status: LoanStatus.Settled);
        var later = Debt(LoanDirection.Borrow, 500_000, Today.AddDays(40));
        var paid = Debt(LoanDirection.Borrow, 500_000, Today.AddDays(2));
        paid.RepaidCents = 500_000;

        LoanForecast.Events([undatedBorrowed, undatedLent, settled, later, paid], Today, Today.AddDays(29)).Should().BeEmpty();
        LoanForecast.Undated([undatedBorrowed, undatedLent, settled]).Should().Be(new UndatedLoansDto(2, 700_000, 300_000));
        LoanForecast.Undated([later]).Should().BeNull();
    }

    [Fact]
    public void Spendable_TakesOutBorrowedDebtsDueInThePeriod_IncludingOverdue_ButNotLentOrLater()
    {
        var due = Debt(LoanDirection.Borrow, 3_000_000, new DateOnly(2026, 11, 25));
        var overdue = Debt(LoanDirection.Borrow, 500_000, Today.AddDays(-3));
        var lent = Debt(LoanDirection.Lend, 1_000_000, new DateOnly(2026, 11, 20));
        var later = Debt(LoanDirection.Borrow, 9_000_000, new DateOnly(2026, 12, 5));

        var result = SpendableCalculator.Compute(new SpendableInput(
            Today, [new SpendableAccount(Account, "Ví", 10_000_000)], [], [], 0, Array.Empty<SpendableGoalDto>(), 0, Rules, 0, [due, overdue, lent, later]));

        result.LoansDueCents.Should().Be(3_500_000);
        result.SpendableCents.Should().Be(6_500_000);
        result.UpcomingBillsCents.Should().Be(0, "D12: nợ tách khỏi hoá đơn");
        result.Loans.Select(d => d.LoanId).Should().Equal(overdue.Id, due.Id);
    }

    [Fact]
    public void Spendable_WithoutDebts_IsUnchanged()
    {
        var result = SpendableCalculator.Compute(new SpendableInput(
            Today, [new SpendableAccount(Account, "Ví", 2_900_000)], [], [], 0, Array.Empty<SpendableGoalDto>(), 0, Rules));

        result.LoansDueCents.Should().Be(0);
        result.Loans.Should().BeEmpty();
        result.SpendableCents.Should().Be(2_900_000);
    }

    /// <summary>Dự báo còn đi tới Agent và LLM: nhãn là chữ cố định, không tên người hay tiêu đề khoản (app tra bằng loanId).</summary>
    [Fact]
    public void EventLabels_NameNoOne()
    {
        var events = LoanForecast.Events(
            [Debt(LoanDirection.Borrow, 1_000_000, Today.AddDays(3), "Nguyễn Văn Hoa"), Debt(LoanDirection.Lend, 1_000_000, Today.AddDays(4), "Trần Lan")],
            Today, Today.AddDays(29));

        events.Select(e => e.Label).Should().Equal(LoanForecast.OutflowLabel, LoanForecast.InflowLabel);
    }
}
