using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class RecurringScheduleTests
{
    [Fact]
    public void Monthly_OnThe31st_ClampsInShortMonthsAndComesBack()
    {
        var feb = RecurringSchedule.Next(new DateOnly(2027, 1, 31), RecurringCadence.Monthly, 31);
        var mar = RecurringSchedule.Next(feb, RecurringCadence.Monthly, 31);

        feb.Should().Be(new DateOnly(2027, 2, 28));
        mar.Should().Be(new DateOnly(2027, 3, 31));
    }

    [Fact]
    public void Weekly_AddsSevenDays()
        => RecurringSchedule.Next(new DateOnly(2026, 11, 2), RecurringCadence.Weekly).Should().Be(new DateOnly(2026, 11, 9));

    [Fact]
    public void Yearly_OnLeapDay_FallsBackToThe28th()
        => RecurringSchedule.Next(new DateOnly(2028, 2, 29), RecurringCadence.Yearly, 29).Should().Be(new DateOnly(2029, 2, 28));

    [Fact]
    public void Occurrences_StartFromTheFirstDueDateInsideTheRange()
    {
        var item = ForecastTestData.Item("Tiền nhà", 4_000_000, new DateOnly(2026, 10, 10));

        RecurringSchedule.Occurrences(item, new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31))
            .Should().Equal(new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 10), new DateOnly(2027, 1, 10));
    }

    // Phase 92 (BR-INS-05): lịch tháng theo anchor_day; tháng ngắn hơn về ngày cuối tháng rồi quay lại ngày gốc.
    [Theory]
    [InlineData(2027, 15, 15, 15, 15, 15)]
    [InlineData(2027, 28, 28, 28, 28, 28)]
    [InlineData(2027, 29, 29, 28, 29, 29)]
    [InlineData(2027, 30, 30, 28, 30, 30)]
    [InlineData(2027, 31, 31, 28, 31, 30)]
    [InlineData(2028, 15, 15, 15, 15, 15)]
    [InlineData(2028, 28, 28, 28, 28, 28)]
    [InlineData(2028, 29, 29, 29, 29, 29)]
    [InlineData(2028, 30, 30, 29, 30, 30)]
    [InlineData(2028, 31, 31, 29, 31, 30)]
    public void Monthly_FollowsTheAnchorDay_ThroughFebruary(int year, int anchor, int jan, int feb, int mar, int apr)
    {
        var item = Installment(new DateOnly(year, 1, jan), anchor);

        RecurringSchedule.Occurrences(item.ToView(), new DateOnly(year, 1, 1), new DateOnly(year, 4, 30))
            .Should().Equal(new DateOnly(year, 1, jan), new DateOnly(year, 2, feb), new DateOnly(year, 3, mar), new DateOnly(year, 4, apr));
    }

    [Fact]
    public void ScenarioD_DueOnThe31st_CreatedInJanuary()
    {
        var item = Installment(new DateOnly(2027, 1, 31), 31);
        var due = item.NextDueDate;
        var dates = new List<DateOnly> { due };
        for (var i = 0; i < 3; i++)
        {
            due = RecurringSchedule.Next(due, item.Cadence, RecurringSchedule.DayOf(item));
            dates.Add(due);
        }

        dates.Should().Equal(new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30));
    }

    [Fact]
    public void DayOf_UsesTheAnchorForMonthly_TheOldRuleWithoutOne_AndNothingForWeekly()
    {
        RecurringSchedule.DayOf(Installment(new DateOnly(2027, 2, 28), 28)).Should().Be(28, "lỗi §13.1: ngày 28 không còn là cuối tháng");
        RecurringSchedule.DayOf(Installment(new DateOnly(2027, 2, 28), null)).Should().Be(31, "khoản chưa có anchor giữ cách suy cũ");
        var weekly = Installment(new DateOnly(2027, 2, 28), null);
        weekly.Cadence = RecurringCadence.Weekly;
        RecurringSchedule.DayOf(weekly).Should().BeNull();
        RecurringSchedule.Next(new DateOnly(2027, 2, 28), RecurringCadence.Weekly, RecurringSchedule.DayOf(weekly)).Should().Be(new DateOnly(2027, 3, 7));
    }

    [Fact]
    public void Previous_UndoesNext_WithTheAnchor()
    {
        var item = Installment(new DateOnly(2027, 3, 31), 31);

        RecurringSchedule.Previous(item.NextDueDate, item.Cadence, RecurringSchedule.DayOf(item)).Should().Be(new DateOnly(2027, 2, 28));
        RecurringSchedule.Previous(new DateOnly(2027, 2, 28), item.Cadence, RecurringSchedule.DayOf(item)).Should().Be(new DateOnly(2027, 1, 31));
    }

    private static RecurringItem Installment(DateOnly due, int? anchor) => new()
    {
        Id = Guid.NewGuid(),
        Direction = RecurringDirection.Outflow,
        Name = "Trả góp",
        CounterpartyKey = "fe credit",
        AmountCents = 1_200_000,
        Cadence = RecurringCadence.Monthly,
        NextDueDate = due,
        AnchorDay = anchor,
        Status = RecurringStatus.Active,
        Source = RecurringSource.Manual,
    };
}
