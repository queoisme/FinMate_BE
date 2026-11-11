using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Bản chụp dự báo, số dư thực tế và chỉ số độ chính xác: các phép tính thuần, kiểm bằng số tính tay.</summary>
public class ForecastAccuracyTests
{
    private static readonly DateOnly Origin = new(2026, 11, 10);
    private static readonly Guid User = Guid.NewGuid();

    /// <summary>Số dư dự kiến giảm 10.000 mỗi ngày từ 1.000.000; có một khoản định kỳ chi 500.000 vào ngày thứ 3.</summary>
    private static CashflowForecastDto Forecast(int lastDay, string confidence = "normal")
    {
        var days = new List<CashflowDayDto>();
        for (var i = 0; i <= lastDay; i++)
        {
            var events = i == 3 ? new List<CashflowEventDto> { new(CashflowEventKinds.RecurringOutflow, "Tiền trọ", -500_000, null) } : [];
            var expected = 1_000_000 - (i * 10_000L);
            days.Add(new CashflowDayDto(Origin.AddDays(i), expected, expected - 50_000, expected + 50_000, 10_000, events));
        }

        return new CashflowForecastDto(
            Origin, Origin.AddDays(lastDay), 1_000_000, days[^1].ExpectedBalanceCents, days[^1].LowBalanceCents, days[^1].HighBalanceCents,
            Origin.AddDays(lastDay), days[^1].ExpectedBalanceCents, confidence, "history", days, []);
    }

    private static ForecastSnapshot Snapshot(int horizon, long start, long predicted, long conservative, long optimistic, long recurringOnly, long? actual, string confidence = "normal")
        => new()
        {
            Id = Guid.NewGuid(), UserId = User, OriginDate = Origin, HorizonDays = horizon, HorizonDate = Origin.AddDays(horizon), Method = ForecastMethods.MedianBand,
            StartBalanceCents = start, PredictedCents = predicted, ConservativeCents = conservative, OptimisticCents = optimistic, RecurringOnlyCents = recurringOnly,
            Confidence = confidence, ActualCents = actual,
        };

    [Fact]
    public void ABuildCreatesOneSnapshotPerHorizonWithTheDayValuesAndTheRecurringOnlyBaseline()
    {
        var snapshots = ForecastSnapshotBuilder.Build(User, Forecast(30), configVersion: 4, DateTimeOffset.UtcNow);

        snapshots.Select(s => s.HorizonDays).Should().Equal(7, 14, 30);
        var week = snapshots[0];
        week.OriginDate.Should().Be(Origin);
        week.HorizonDate.Should().Be(Origin.AddDays(7));
        week.PredictedCents.Should().Be(1_000_000 - 70_000);
        (week.ConservativeCents, week.OptimisticCents).Should().Be((880_000, 980_000));
        week.RecurringOnlyCents.Should().Be(1_000_000 - 500_000);   // chỉ khoản định kỳ, chi linh hoạt coi như 0
        (week.ConfigVersion, week.Method, week.StartBalanceCents).Should().Be((4, ForecastMethods.MedianBand, 1_000_000));
        snapshots.Should().OnlyContain(s => s.ActualCents == null);
    }

    [Fact]
    public void AForecastShorterThanAHorizonSkipsThatHorizon()
    {
        var snapshots = ForecastSnapshotBuilder.Build(User, Forecast(10), configVersion: 1, DateTimeOffset.UtcNow);

        snapshots.Select(s => s.HorizonDays).Should().Equal(7);
    }

    [Fact]
    public void ActualNetFlowCountsCreditsAndDebitsOfIncludedAccountsInsideTheRangeOnly()
    {
        var included = Guid.NewGuid();
        var other = Guid.NewGuid();
        ForecastTransaction Tx(Guid account, long amount, TransactionType type, int dayOffset)
            => new(Guid.NewGuid(), account, null, amount, type, DateTimeOffset.UtcNow, Origin.AddDays(dayOffset), null, null, false);
        var transactions = new[]
        {
            Tx(included, 8_000_000, TransactionType.Credit, 2),
            Tx(included, 300_000, TransactionType.Debit, 3),
            Tx(included, 50_000, TransactionType.Debit, 9),     // sau ngày của mốc
            Tx(other, 1_000_000, TransactionType.Debit, 2),     // ví không được tính
            Tx(included, 20_000, TransactionType.Debit, -1),    // trước ngày gốc
        };

        ForecastActuals.NetFlow(transactions, new HashSet<Guid> { included }, Origin, Origin.AddDays(7)).Should().Be(8_000_000 - 300_000);
    }

    [Fact]
    public void TheCalculatorMeasuresMaeCoverageAndTheTwoBaselinesByHand()
    {
        // Hai bản chụp mốc 14 ngày. Sai số dự báo 100 và 300; sai số "số dư không đổi" 600 và 200; chỉ khoản định kỳ lệch 150 và 50.
        var rows = ForecastAccuracyCalculator.Compute(
        [
            Snapshot(14, start: 1_500, predicted: 1_000, conservative: 800, optimistic: 1_200, recurringOnly: 1_050, actual: 900),   // trong dải
            Snapshot(14, start: 2_500, predicted: 2_000, conservative: 1_800, optimistic: 2_100, recurringOnly: 2_350, actual: 2_300),   // ngoài dải
        ]);

        var all = rows.Single(r => r.HorizonDays == 14 && r.Confidence == "all");
        all.Samples.Should().Be(2);
        all.MaeCents.Should().Be(200);
        all.MedianAbsErrorCents.Should().Be(200);
        all.MaeHoldCents.Should().Be(400);
        all.MaeRecurringOnlyCents.Should().Be(100);
        all.CoveragePercent.Should().Be(50.0);
        all.MeanBandWidthCents.Should().Be((400 + 300) / 2);
        all.RatioVsHold.Should().Be(0.5);
        all.RatioVsRecurringOnly.Should().Be(2.0);
    }

    [Fact]
    public void TheColdStartGroupIsReportedApartFromTheRest()
    {
        var rows = ForecastAccuracyCalculator.Compute(
        [
            Snapshot(7, 1_000, 900, 800, 1_000, 950, actual: 500, confidence: "low"),
            Snapshot(7, 1_000, 900, 800, 1_000, 950, actual: 880, confidence: "normal"),
        ]);

        rows.Single(r => r.Confidence == "low").MaeCents.Should().Be(400);
        rows.Single(r => r.Confidence == "normal").MaeCents.Should().Be(20);
        rows.Single(r => r.Confidence == "all").Samples.Should().Be(2);
    }

    [Fact]
    public void SnapshotsWithoutAnActualAreIgnoredAndNoDataGivesNoRows()
    {
        ForecastAccuracyCalculator.Compute([Snapshot(7, 1_000, 900, 800, 1_000, 950, actual: null)]).Should().BeEmpty();
        ForecastAccuracyCalculator.Compute([]).Should().BeEmpty();
    }

    [Fact]
    public void ARatioIsNullWhenTheBaselineIsPerfect()
    {
        // Số dư thật đúng bằng số dư đầu: mốc "không đổi" không sai chút nào, tỉ lệ chia cho 0 nên để trống thay vì ra số vô nghĩa.
        var row = ForecastAccuracyCalculator.Compute([Snapshot(7, 1_000, 900, 800, 1_000, 950, actual: 1_000)]).Single(r => r.Confidence == "all");

        row.MaeHoldCents.Should().Be(0);
        row.RatioVsHold.Should().BeNull();
    }
}
