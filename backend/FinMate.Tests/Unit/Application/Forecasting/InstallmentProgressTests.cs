using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 92: số kỳ đã trả / còn lại / kỳ cuối tính ra từ entity (BR-INS-03, 10), và hoàn thành / mở lại (BR-INS-06, 08).</summary>
public class InstallmentProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

    private static RecurringItem Item(int? total = 12, int prepaid = 0, int live = 0, DateOnly? due = null, DateOnly? end = null,
        RecurringStatus status = RecurringStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Direction = RecurringDirection.Outflow,
        Name = "Trả góp",
        CounterpartyKey = "fe credit",
        AmountCents = 1_200_000,
        Cadence = RecurringCadence.Monthly,
        NextDueDate = due ?? new DateOnly(2026, 11, 15),
        AnchorDay = 15,
        TotalOccurrences = total,
        EndDate = end,
        PrepaidOccurrences = prepaid,
        LivePaymentCount = live,
        Status = status,
        CompletedAt = status == RecurringStatus.Completed ? Now : null,
    };

    [Fact]
    public void ScenarioA_NinePrepaid_ThreeLeft_3_6Million()
    {
        var progress = InstallmentProgress.Of(Item(prepaid: 9), Today);

        progress.Should().Be(new InstallmentProgress(9, 3, 3_600_000, new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public void PaidCountsLiveLinksPlusPrepaid()
        => InstallmentProgress.Of(Item(prepaid: 4, live: 5), Today).Paid.Should().Be(9);

    [Fact]
    public void OpenEnded_HasNoRemainingOrFinal()
        => InstallmentProgress.Of(Item(total: null, live: 3), Today).Should().Be(new InstallmentProgress(3, null, null, null));

    [Fact]
    public void EndDate_CountsTheDueDatesUpToIt()
        => InstallmentProgress.Of(Item(total: null, end: new DateOnly(2027, 2, 20)), Today)
            .Should().Be(new InstallmentProgress(0, 4, 4_800_000, new DateOnly(2027, 2, 15)));

    [Fact]
    public void Finished_HasNoFinalDueDate()
        => InstallmentProgress.Of(Item(live: 12), Today).Should().Be(new InstallmentProgress(12, 0, 0, null));

    [Fact]
    public void S1_FinalDueDate_MovesLater_WhileAnInstallmentIsOverdue()
        => InstallmentProgress.Of(Item(prepaid: 11, due: Today.AddDays(-20)), Today).FinalDueDate.Should().Be(Today.AddDays(1));

    [Fact]
    public void PaidReachingTheTotal_Completes()
    {
        var item = Item(prepaid: 9, live: 3);

        RecurringCompletion.Apply(item, Today, Now).Should().BeTrue();

        item.Status.Should().Be(RecurringStatus.Completed);
        item.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public void LosingAPayment_Reopens()
    {
        var item = Item(prepaid: 9, live: 2, status: RecurringStatus.Completed);

        RecurringCompletion.Apply(item, Today, Now).Should().BeTrue();

        item.Status.Should().Be(RecurringStatus.Active);
        item.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void SettledEarly_StaysCompleted_EvenWithInstallmentsLeft()
    {
        var item = Item(live: 6, status: RecurringStatus.Completed);
        item.SettledEarly = true;

        RecurringCompletion.Apply(item, Today, Now).Should().BeFalse();
        item.Status.Should().Be(RecurringStatus.Completed);
    }

    [Fact]
    public void EndDate_CompletesWhenPassedOrAllPaid()
    {
        var passed = Item(total: null, end: Today.AddDays(-1), due: Today.AddDays(-5));
        var allPaid = Item(total: null, end: Today.AddDays(10), due: Today.AddDays(20));
        var running = Item(total: null, end: Today.AddDays(40));

        RecurringCompletion.Apply(passed, Today, Now).Should().BeTrue();
        RecurringCompletion.Apply(allPaid, Today, Now).Should().BeTrue();
        RecurringCompletion.Apply(running, Today, Now).Should().BeFalse();
        running.Status.Should().Be(RecurringStatus.Active);
    }

    [Fact]
    public void OnlyActiveOrPausedComplete_SuggestedOrCancelledAreLeftAlone()
    {
        var suggested = Item(live: 12, status: RecurringStatus.Suggested);
        var cancelled = Item(live: 12, status: RecurringStatus.Cancelled);
        var paused = Item(live: 12, status: RecurringStatus.Paused);

        RecurringCompletion.Apply(suggested, Today, Now).Should().BeFalse();
        RecurringCompletion.Apply(cancelled, Today, Now).Should().BeFalse();
        RecurringCompletion.Apply(paused, Today, Now).Should().BeTrue();
    }

    [Fact]
    public void OpenEnded_NeverCompletes()
        => RecurringCompletion.Apply(Item(total: null, live: 100), Today, Now).Should().BeFalse();
}
