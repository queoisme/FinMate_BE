using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class CashflowEngineTests
{
    private static SpendingBaseline Flat(long perDay)
    {
        var v = Enumerable.Repeat(perDay, 7).ToArray();
        return new SpendingBaseline(v, v.ToArray(), v.ToArray(), ForecastConfidence.Normal, 60, SpendingBaseline.BasisHistory);
    }

    private static CashflowInput Input(
        long balance, long perDay, IReadOnlyList<CashflowEvent>? events = null, long spentToday = 0, IReadOnlyList<ForecastHoliday>? holidays = null)
        => new(Today, Today.AddDays(9), balance, Flat(perDay), spentToday, events ?? [], holidays ?? []);

    [Fact]
    public void Balance_FallsByTheDailySpend()
    {
        var result = CashflowEngine.Project(Input(1_000_000, 100_000));

        result.Days.Should().HaveCount(10);
        result.Days[0].ExpectedBalanceCents.Should().Be(900_000);
        result.EndBalanceCents.Should().Be(0);
    }

    [Fact]
    public void SpendAlreadyMadeToday_IsNotSubtractedAgain()
        => CashflowEngine.Project(Input(1_000_000, 100_000, spentToday: 70_000)).Days[0].ExpectedBalanceCents.Should().Be(970_000);

    [Fact]
    public void KnownEvents_LandOnTheirDay_AndTheLowestDayIsReported()
    {
        var events = new[]
        {
            new CashflowEvent(Today.AddDays(3), -4_000_000, "Tiền nhà", CashflowEventKinds.RecurringOutflow, null),
            new CashflowEvent(Today.AddDays(5), 15_000_000, "Lương", CashflowEventKinds.RecurringInflow, null),
        };

        var result = CashflowEngine.Project(Input(5_000_000, 100_000, events));

        result.Days[3].ExpectedBalanceCents.Should().Be(5_000_000 - (4 * 100_000) - 4_000_000);
        result.LowestDay.Should().Be(Today.AddDays(4));
        result.MainCauses[0].Label.Should().Be("Tiền nhà");
    }

    [Fact]
    public void HolidayMultiplier_RaisesFlexibleSpending()
    {
        var tet = new ForecastHoliday { Name = "Tết", Start = Today.AddDays(1), End = Today.AddDays(1), MultiplierPercent = 150 };

        CashflowEngine.Project(Input(1_000_000, 100_000, holidays: [tet])).Days[1].FlexibleSpendCents.Should().Be(150_000);
    }

    [Fact]
    public void Scenario_IsJustAnotherInput_AndChangesNothingElse()
    {
        var baseInput = Input(1_000_000, 50_000);
        var before = CashflowEngine.Project(baseInput);
        var withShoes = CashflowEngine.Project(baseInput with
        {
            Events = [new CashflowEvent(Today.AddDays(2), -1_500_000, "Giày", CashflowEventKinds.Scenario, null)],
        });

        withShoes.EndBalanceCents.Should().Be(before.EndBalanceCents - 1_500_000);
        CashflowEngine.Project(baseInput).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void RecurringEvents_SkipAlreadyPaidDueDates()
    {
        var rent = Item("Tiền nhà", 4_000_000, Today.AddDays(1));

        var events = CashflowEngine.RecurringEvents([rent], [Debit(Today.AddDays(-1), 4_000_000, "Tiền nhà")], Today, Today.AddDays(40), Rules);

        events.Should().ContainSingle().Which.Date.Should().Be(Today.AddDays(1).AddMonths(1));
    }

    [Fact]
    public void RecurringEvents_OverdueWithinGrace_IsExpectedToday_AndBeyondGraceIsDropped()
    {
        var recent = Item("Điện", 600_000, Today.AddDays(-3));
        var stale = Item("Gym cũ", 300_000, Today.AddDays(-20));

        var events = CashflowEngine.RecurringEvents([recent, stale], [], Today, Today.AddDays(5), Rules);

        events.Should().ContainSingle().Which.Should().Match<CashflowEvent>(e => e.Label == "Điện" && e.Date == Today && e.AmountCents == -600_000);
    }

    [Fact]
    public void RecurringEvents_OverdueIncome_IsNotCountedOnToday()
        => CashflowEngine.RecurringEvents(
                [Item("Lương", 15_000_000, Today.AddDays(-2), RecurringDirection.Inflow)], [], Today, Today.AddDays(20), Rules)
            .Should().BeEmpty("trông vào tiền chưa về là lạc quan; lần kế tiếp ở tháng sau nằm ngoài kỳ");

    [Fact]
    public void RecurringEvents_BillPaidAtANewPrice_IsStillTreatedAsPaid()
        => CashflowEngine.RecurringEvents([Item("Netflix", 260_000, Today.AddDays(1))], [Debit(Today, 330_000, "Netflix")], Today, Today.AddDays(10), Rules)
            .Should().BeEmpty();

    [Fact]
    public void RecurringEvents_Income_IsPositive()
        => CashflowEngine.RecurringEvents(
                [Item("Lương", 15_000_000, Today.AddDays(3), RecurringDirection.Inflow)], [], Today, Today.AddDays(5), Rules)
            .Single().AmountCents.Should().Be(15_000_000);

    private static IncomeBaseline Income(long expected, long conservative, long optimistic, string basis = SpendingBaseline.BasisHistory)
        => new(Enumerable.Repeat(expected, 7).ToArray(), Enumerable.Repeat(conservative, 7).ToArray(), Enumerable.Repeat(optimistic, 7).ToArray(), basis);

    [Fact]
    public void IrregularIncome_IsAddedToEachLine_AndReportedPerDay()
    {
        // Phase 97: thu không định kỳ vào đường số dư; dự kiến / thận trọng / lạc quan dùng mức tương ứng.
        var input = Input(1_000_000, 100_000) with { Income = Income(80_000, 60_000, 90_000) };

        var result = CashflowEngine.Project(input);

        result.Days[0].ExpectedBalanceCents.Should().Be(1_000_000 - 100_000 + 80_000);
        result.Days[0].LowBalanceCents.Should().Be(1_000_000 - 100_000 + 60_000);
        result.Days[0].HighBalanceCents.Should().Be(1_000_000 - 100_000 + 90_000);
        result.Days.Should().OnlyContain(d => d.IrregularIncomeCents == 80_000);
        result.IncomeBasis.Should().Be(SpendingBaseline.BasisHistory);
    }

    [Fact]
    public void IrregularIncomeAlreadyReceivedToday_IsNotAddedAgain()
    {
        var input = Input(1_000_000, 0) with { Income = Income(80_000, 60_000, 90_000), ReceivedTodayIrregularCents = 70_000 };

        var day = CashflowEngine.Project(input).Days[0];

        day.IrregularIncomeCents.Should().Be(10_000);
        day.LowBalanceCents.Should().Be(1_000_000, "phần thận trọng 60.000đ đã về hết trong 70.000đ hôm nay");
        day.HighBalanceCents.Should().Be(1_020_000, "90.000đ lạc quan trừ 70.000đ đã về");
    }

    [Fact]
    public void TheHolidayMultiplier_ScalesSpendingButNotIncome()
    {
        var holiday = new ForecastHoliday { Name = "Tết", Start = Today, End = Today.AddDays(9), MultiplierPercent = 200 };
        var input = Input(1_000_000, 100_000, holidays: [holiday]) with { Income = Income(80_000, 80_000, 80_000) };

        var day = CashflowEngine.Project(input).Days[0];

        day.FlexibleSpendCents.Should().Be(200_000);
        day.IrregularIncomeCents.Should().Be(80_000);
    }

    [Fact]
    public void WithoutAnIncomeBaseline_TheForecastIsUnchanged()
    {
        var plain = CashflowEngine.Project(Input(1_000_000, 100_000));

        plain.Days.Should().OnlyContain(d => d.IrregularIncomeCents == 0);
        plain.IncomeBasis.Should().BeNull();
        plain.Days[0].ExpectedBalanceCents.Should().Be(900_000);
    }

    // ---- Phase 97 tầng 2: dải của tổng dòng tiền linh hoạt ròng (B2) ----

    [Fact]
    public void WithASumBand_LowAndHighAreTheExpectedLine_PlusMinusZSigmaOfTheSumSoFar()
    {
        var band = new NetFlowBand(DailyVariance: 100_000_000_000, HistoryDays: 90);
        var result = CashflowEngine.Project(Input(10_000_000, 100_000) with { Band = band });

        for (var i = 0; i < result.Days.Count; i++)
        {
            var day = result.Days[i];
            var l = i + 1;
            var half = (long)Math.Round(NetFlowBand.Z * Math.Sqrt(1e11 * l + 1e11 / 90 * l * l), MidpointRounding.AwayFromZero);
            day.LowBalanceCents.Should().Be(day.ExpectedBalanceCents - half);
            day.HighBalanceCents.Should().Be(day.ExpectedBalanceCents + half);
        }
    }

    [Fact]
    public void TheSumBand_GrowsSlowerThanAddingDailyQuantiles()
    {
        // Cộng phân vị từng ngày: nửa độ rộng tăng theo L. Dải của tổng: theo √L (cộng phần sai số của mức trung bình, theo L/√n).
        var band = new NetFlowBand(DailyVariance: 1e10, HistoryDays: 90);

        (band.HalfWidth(30) / (double)band.HalfWidth(1)).Should().BeLessThan(30.0 / 3);
        band.HalfWidth(30).Should().BeGreaterThan(band.HalfWidth(14));
    }

    [Fact]
    public void WithASumBand_ALoanDueToUs_IsStillAddedToTheOptimisticLineOnly()
    {
        var loan = new CashflowEvent(Today.AddDays(2), 3_000_000, "Khoản cho vay", CashflowEventKinds.LoanInflow, null, CashflowBand.OptimisticOnly);
        var band = new NetFlowBand(DailyVariance: 0, HistoryDays: 90);

        var result = CashflowEngine.Project(Input(10_000_000, 100_000, [loan]) with { Band = band });

        result.Days[1].HighBalanceCents.Should().Be(result.Days[1].ExpectedBalanceCents);
        result.Days[2].HighBalanceCents.Should().Be(result.Days[2].ExpectedBalanceCents + 3_000_000);
        result.Days[^1].HighBalanceCents.Should().Be(result.Days[^1].ExpectedBalanceCents + 3_000_000);
        result.Days.Should().OnlyContain(d => d.LowBalanceCents == d.ExpectedBalanceCents);
    }

    [Fact]
    public void NetFlowBand_IsTheResidualVarianceAroundTheWeekdayMean_OfNetFlexibleFlow()
    {
        // 91 ngày: chi 100.000đ mỗi ngày, thêm 700.000đ mỗi Chủ nhật (mẫu theo thứ cố định, không có bất định), một khoản thu dự án 2.800.000đ mỗi 14 ngày.
        var history = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), Today.AddDays(-i).DayOfWeek == DayOfWeek.Sunday ? 800_000 : 100_000, "Quán ăn")).ToList();
        var steady = NetFlowBand.From(history, [], Today, Rules)!;
        steady.DailyVariance.Should().Be(0, "mẫu theo thứ lặp lại y hệt mỗi tuần");
        steady.HistoryDays.Should().Be(91);

        history.AddRange(Enumerable.Range(1, 91).Where(i => i % 14 == 0).Select(i => Credit(Today.AddDays(-i), 2_800_000, "Khách hàng A")));
        NetFlowBand.From(history, [], Today, Rules)!.DailyVariance.Should().BeGreaterThan(0, "thu không đều là bất định, nằm trong σ của dòng tiền ròng");
    }

    [Fact]
    public void NetFlowBand_IgnoresRecurringPaymentsExcludedRowsLoansAndRefunds_AndNeedsEnoughHistory()
    {
        var history = Enumerable.Range(1, 91).Select(i => Debit(Today.AddDays(-i), 100_000, "Quán ăn")).ToList();
        history.Add(Debit(new DateOnly(2026, 10, 5), 4_000_000, "Tiền nhà"));
        history.Add(Debit(Today.AddDays(-3), 9_000_000, "Tiệc", excluded: true));
        history.Add(Credit(Today.AddDays(-4), 5_000_000, "Người thân") with { IsLoan = true });
        history.Add(Credit(Today.AddDays(-5), 5_000_000, "Shop") with { Relation = TransactionRelation.Refund });

        NetFlowBand.From(history, [Item("Tiền nhà", 5_000_000, new DateOnly(2026, 11, 5))], Today, Rules)!.DailyVariance.Should().Be(0);
        NetFlowBand.From(history.Take(10).ToList(), [], Today, Rules).Should().BeNull("dưới minHistoryDays: engine giữ các đường của tầng 1");
    }
}
