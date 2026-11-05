using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class RecurringTrackerTests
{
    private static RecurringItem Entity(RecurringStatus status, DateOnly due, int occurrences = 3, RecurringSource source = RecurringSource.Detected) => new()
    {
        Id = Guid.NewGuid(),
        Direction = RecurringDirection.Outflow,
        Name = "Netflix",
        CounterpartyKey = "netflix",
        AmountCents = 260_000,
        Cadence = RecurringCadence.Monthly,
        NextDueDate = due,
        Status = status,
        Source = source,
        Occurrences = occurrences,
    };

    [Fact]
    public void PaymentAroundTheDueDate_AdvancesToTheNextMonth()
    {
        var item = Entity(RecurringStatus.Active, Today);

        var changed = RecurringTracker.Advance(item, [Debit(Today.AddDays(-2), 270_000, "Netflix")], Rules, DateTimeOffset.UtcNow);

        changed.Should().BeTrue();
        item.NextDueDate.Should().Be(Today.AddMonths(1));
        item.Occurrences.Should().Be(4);
        item.AmountCents.Should().Be(270_000, "khoản tự phát hiện theo số tiền thật mới nhất");
    }

    [Fact]
    public void EndOfMonthBill_ReturnsToTheLastDayAfterAShortMonth()
    {
        var item = Entity(RecurringStatus.Active, new DateOnly(2027, 2, 28));

        RecurringTracker.Advance(item, [Debit(new DateOnly(2027, 2, 27), 260_000, "Netflix")], Rules, DateTimeOffset.UtcNow);

        item.NextDueDate.Should().Be(new DateOnly(2027, 3, 31));
    }

    private static RecurringItem Rent(long amount = 5_000_000)
    {
        var item = Entity(RecurringStatus.Active, Today);
        item.AmountCents = amount;
        return item;
    }

    [Fact]
    public void AFixedPriceThatRisesWithinTheTolerance_IsStillReported()
    {
        var item = Rent();
        var changes = new List<PriceChange>();
        var history = new[]
        {
            Debit(Today.AddMonths(-2), 5_000_000, "Netflix"),
            Debit(Today.AddMonths(-1), 5_000_000, "Netflix"),
            Debit(Today, 5_400_000, "Netflix"),
        };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().ContainSingle(c => c.OldAmountCents == 5_000_000 && c.NewAmountCents == 5_400_000);
    }

    [Fact]
    public void AManualItemIsReportedButKeepsTheAmountAfterAOneOffOverpayment()
    {
        var item = Entity(RecurringStatus.Active, Today, source: RecurringSource.Manual);
        item.AmountCents = 5_000_000;
        var changes = new List<PriceChange>();
        var history = new[]
        {
            Debit(Today.AddMonths(-2), 5_000_000, "Netflix"),
            Debit(Today.AddMonths(-1), 5_000_000, "Netflix"),
            Debit(Today, 5_050_000, "Netflix"),
        };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().ContainSingle();
        item.AmountCents.Should().Be(5_000_000);
    }

    [Fact]
    public void TwoSameDayChargesOrATinyRiseAreNotAFixedPriceRise()
    {
        var sameDay = new[] { Debit(Today.AddMonths(-1), 5_000_000, "Netflix"), Debit(Today.AddMonths(-1), 5_000_000, "Netflix"), Debit(Today, 5_400_000, "Netflix") };
        var tiny = new[] { Debit(Today.AddMonths(-2), 5_000_000, "Netflix"), Debit(Today.AddMonths(-1), 5_000_000, "Netflix"), Debit(Today, 5_010_000, "Netflix") };
        var first = new List<PriceChange>();
        var second = new List<PriceChange>();

        RecurringTracker.Advance(Rent(), sameDay, Rules, DateTimeOffset.UtcNow, first);
        RecurringTracker.Advance(Rent(), tiny, Rules, DateTimeOffset.UtcNow, second);

        first.Should().BeEmpty();
        second.Should().BeEmpty();
    }

    [Fact]
    public void AWeeklyItemIsNotTreatedAsFixedPrice()
    {
        var item = Rent(45_000);
        item.Cadence = RecurringCadence.Weekly;
        var changes = new List<PriceChange>();
        var history = new[] { Debit(Today.AddDays(-14), 45_000, "Netflix"), Debit(Today.AddDays(-7), 45_000, "Netflix"), Debit(Today, 46_000, "Netflix") };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void ABillThatVariesWithinTheTolerance_IsNotReported()
    {
        var item = Rent(480_000);
        var changes = new List<PriceChange>();
        var history = new[]
        {
            Debit(Today.AddMonths(-2), 450_000, "Netflix"),
            Debit(Today.AddMonths(-1), 480_000, "Netflix"),
            Debit(Today, 510_000, "Netflix"),
        };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void AFixedPriceThatFallsWithinTheTolerance_IsNotReported()
    {
        var item = Rent();
        var changes = new List<PriceChange>();
        var history = new[]
        {
            Debit(Today.AddMonths(-2), 5_000_000, "Netflix"),
            Debit(Today.AddMonths(-1), 5_000_000, "Netflix"),
            Debit(Today, 4_800_000, "Netflix"),
        };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void OnePriorPaymentIsNotEnoughToCallItFixed()
    {
        var item = Rent();
        var changes = new List<PriceChange>();
        var history = new[]
        {
            Debit(Today.AddMonths(-1), 5_000_000, "Netflix"),
            Debit(Today, 5_400_000, "Netflix"),
        };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void PaymentWithANewPrice_AdvancesAndReportsThePriceChange()
    {
        var item = Entity(RecurringStatus.Active, Today, source: RecurringSource.Manual);
        var changes = new List<PriceChange>();

        RecurringTracker.Advance(item, [Debit(Today, 330_000, "Netflix")], Rules, DateTimeOffset.UtcNow, changes);

        changes.Should().ContainSingle(c => c.OldAmountCents == 260_000 && c.NewAmountCents == 330_000);
        item.AmountCents.Should().Be(330_000, "giá đổi thật thì theo giá mới, cả với khoản user tự thêm");
        item.NextDueDate.Should().Be(Today.AddMonths(1));
    }

    [Fact]
    public void ManualItem_KeepsTheAmountTheUserEntered()
    {
        var item = Entity(RecurringStatus.Active, Today, source: RecurringSource.Manual);

        RecurringTracker.Advance(item, [Debit(Today, 270_000, "Netflix")], Rules, DateTimeOffset.UtcNow);

        item.AmountCents.Should().Be(260_000);
    }

    [Fact]
    public void SamePaymentSeenTwice_IsCountedOnce()
    {
        var item = Entity(RecurringStatus.Active, Today);
        var history = new[] { Debit(Today, 260_000, "Netflix") };

        RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow);
        var again = RecurringTracker.Advance(item, history, Rules, DateTimeOffset.UtcNow);

        again.Should().BeFalse();
        item.Occurrences.Should().Be(4);
    }

    [Fact]
    public void SuggestionThatKeepsRecurring_BecomesActive()
    {
        var item = Entity(RecurringStatus.Suggested, Today, occurrences: 2);

        RecurringTracker.Advance(item, [Debit(Today, 260_000, "Netflix")], Rules, DateTimeOffset.UtcNow);

        item.Status.Should().Be(RecurringStatus.Active);
    }

    [Fact]
    public void RejectedItem_IsNeverTouched()
    {
        var item = Entity(RecurringStatus.Rejected, Today);

        RecurringTracker.Advance(item, [Debit(Today, 260_000, "Netflix")], Rules, DateTimeOffset.UtcNow).Should().BeFalse();
        item.NextDueDate.Should().Be(Today);
    }
}
