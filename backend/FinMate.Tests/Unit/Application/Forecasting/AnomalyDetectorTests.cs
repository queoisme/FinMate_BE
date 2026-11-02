using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class AnomalyDetectorTests
{
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly AnomalyRules AnomalyRules = AnomalyRules.From(new ForecastSettings());

    private static ForecastTransaction Spend(
        int daysAgo, long amount, string? merchant, Guid? category = null, int hour = 12, int minute = 0, Guid? account = null, bool excluded = false)
    {
        var date = Today.AddDays(-daysAgo);
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), VietnamTime.Offset);
        return new ForecastTransaction(
            Guid.NewGuid(), account ?? Account, category ?? Food, amount, TransactionType.Debit, at, date, merchant, CounterpartyKey.From(merchant), excluded);
    }

    /// <summary>6 bữa ăn 100k ở 6 quán khác nhau trong tuần qua: trung vị danh mục 100k, chưa quán nào đủ 3 lần.</summary>
    private static List<ForecastTransaction> Meals() => Enumerable.Range(1, 6).Select(i => Spend(i, 100_000, $"Quán {i}")).ToList();

    private static AnomalyFinding? Evaluate(
        ForecastTransaction candidate, IEnumerable<ForecastTransaction> history,
        IReadOnlyList<PriorNormal>? normals = null, IReadOnlyList<RecurringItemView>? items = null)
        => AnomalyDetector.Evaluate(candidate, history.Append(candidate).ToList(), items ?? [], normals ?? [], Rules, AnomalyRules);

    [Fact]
    public void FarAboveTheCategoryMedian_IsACategorySpike()
    {
        var finding = Evaluate(Spend(0, 400_000, "Nhà hàng mới"), Meals())!;

        finding.Kind.Should().Be(AnomalyKind.CategorySpike);
        finding.TypicalCents.Should().Be(100_000);
        finding.RatioPercent.Should().Be(400);
        finding.Severity.Should().Be(AnomalySeverity.Normal);
    }

    [Fact]
    public void FiveTimesTheMedian_IsHighSeverity()
        => Evaluate(Spend(0, 600_000, "Nhà hàng mới"), Meals())!.Severity.Should().Be(AnomalySeverity.High);

    [Fact]
    public void TooFewSamples_IsNotJudged()
        => Evaluate(Spend(0, 900_000, "Nhà hàng mới"), Meals().Take(4)).Should().BeNull();

    [Fact]
    public void UnderTheMinimumAmount_IsNeverAnomalous()
    {
        // Gấp 5 lần trung vị 30k nhưng chỉ 150k < 200k: khoản nhỏ không đáng làm phiền.
        var cheap = Enumerable.Range(1, 6).Select(i => Spend(i, 30_000, $"Quán {i}"));
        Evaluate(Spend(0, 150_000, "Quán mới"), cheap).Should().BeNull();
    }

    [Fact]
    public void AboveTheUsualAtTheSameMerchant_IsAMerchantSpike()
    {
        var history = Meals().Concat(Enumerable.Range(1, 3).Select(i => Spend(i * 7, 80_000, "Grab")));

        var finding = Evaluate(Spend(0, 320_000, "Grab"), history)!;

        finding.Kind.Should().Be(AnomalyKind.MerchantSpike);
        finding.TypicalCents.Should().Be(80_000);
    }

    [Fact]
    public void SameAmountSameMerchantWithinMinutes_IsADuplicate_OnlyTheSecondOne()
    {
        var first = Spend(0, 250_000, "Điện máy X", hour: 10, minute: 0);
        var second = Spend(0, 250_000, "Điện máy X", hour: 10, minute: 4);
        var history = new[] { first, second };

        AnomalyDetector.Evaluate(second, history, [], [], Rules, AnomalyRules)!.Kind.Should().Be(AnomalyKind.Duplicate);
        AnomalyDetector.Evaluate(first, history, [], [], Rules, AnomalyRules).Should().BeNull();
    }

    [Fact]
    public void SameAmountOnADifferentAccount_IsNotADuplicate()
    {
        var first = Spend(0, 250_000, "Điện máy X", hour: 10);
        var second = Spend(0, 250_000, "Điện máy X", hour: 10, minute: 2, account: Guid.NewGuid());

        AnomalyDetector.Evaluate(second, [first, second], [], [], Rules, AnomalyRules).Should().BeNull();
    }

    [Fact]
    public void SameAmountHoursApart_IsNotADuplicate()
    {
        var first = Spend(0, 250_000, "Điện máy X", hour: 8);
        var second = Spend(0, 250_000, "Điện máy X", hour: 11);

        AnomalyDetector.Evaluate(second, [first, second], [], [], Rules, AnomalyRules).Should().BeNull();
    }

    [Fact]
    public void ConfirmedNormalBefore_ForTheSameCategoryAndMerchant_IsNotFlaggedAgain()
    {
        var candidate = Spend(0, 450_000, "Nhà hàng mới");
        var normals = new[] { new PriorNormal(Food, candidate.CounterpartyKey, 400_000) };

        Evaluate(candidate, Meals(), normals).Should().BeNull();
    }

    [Fact]
    public void ConfirmedNormalBefore_ButMuchLargerNow_IsStillFlagged()
    {
        var candidate = Spend(0, 900_000, "Nhà hàng mới");
        var normals = new[] { new PriorNormal(Food, candidate.CounterpartyKey, 400_000) };

        Evaluate(candidate, Meals(), normals).Should().NotBeNull();
    }

    [Fact]
    public void ExcludedOrCredit_IsNotJudged()
        => Evaluate(Spend(0, 900_000, "Nhà hàng mới", excluded: true), Meals()).Should().BeNull();

    [Fact]
    public void PaymentOfATrackedRecurringItem_IsLeftToThePriceChangeAlert()
    {
        var rent = Item("Tiền nhà", 4_000_000, Today);
        var candidate = Spend(0, 4_000_000, "Tiền nhà");

        Evaluate(candidate, Meals(), items: [rent]).Should().BeNull();
    }
}
