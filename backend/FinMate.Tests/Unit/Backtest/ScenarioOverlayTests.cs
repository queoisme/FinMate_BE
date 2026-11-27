using FinMate.Backtest.Generation;
using FinMate.Domain.Enums;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

/// <summary>Đặc tả bộ sinh §6: mỗi lớp phủ chỉ đổi đúng phần nó cài; ngoài phần đó, thế giới giống K0 từng giao dịch.</summary>
public class ScenarioOverlayTests
{
    private static readonly int[] Seeds = [0, 7, 13];

    public static TheoryData<string, int> PersonaSeeds()
    {
        var data = new TheoryData<string, int>();
        foreach (var persona in GeneratorSpec.Personas)
        {
            foreach (var seed in Seeds)
            {
                data.Add(persona, seed);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K1_AddsExactlyTwoLargeOneOffs_AndChangesNothingElse(string persona, int seed)
    {
        var (k0, k1, diff) = Compare(persona, seed, ScenarioCodes.K1);

        diff.Removed.Should().BeEmpty();
        diff.Changed.Should().BeEmpty();
        diff.Added.Should().HaveCount(2).And.OnlyContain(t => t.InjectedAnomaly && t.Label == TxLabel.Scenario && t.Type == TransactionType.Debit);
        diff.Added.Should().OnlyContain(t => t.AmountCents >= 3_000_000 && t.AmountCents <= 10_000_000);
        k1.EventDays.Should().HaveCount(2);
        Math.Abs(k1.EventDays[1].DayNumber - k1.EventDays[0].DayNumber).Should().BeGreaterThanOrEqualTo(60);
        k1.EventDays.Should().OnlyContain(d => d >= GeneratorSpec.EvaluationStart && d <= GeneratorSpec.EvaluationEnd);
        k0.EventDays.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K2_OnlyMovesTheMainIncome_AndSpendingThatFollowsIt(string persona, int seed)
    {
        var (_, k2, diff) = Compare(persona, seed, ScenarioCodes.K2);
        var firstEvent = k2.EventDays.Min();

        foreach (var t in diff.Removed.Concat(diff.Added).Concat(diff.Changed.Select(c => c.After)))
        {
            var isIncome = t.Type == TransactionType.Credit && t.Label is TxLabel.Recurring or TxLabel.IncomeIrregular or TxLabel.Scenario;
            var isFollowOn = t.Label == TxLabel.Flexible || (persona == Persona.SmallTrader && t.Merchant == "Nộp tiền");
            (isIncome || isFollowOn).Should().BeTrue($"{t.Label} {t.Merchant} {t.Date} không thuộc phần K2 được đổi");
            if (isFollowOn)
            {
                // Lương dời khỏi T7/CN về T6 (tới 2 ngày) và trợ cấp lệch tới 3 ngày, nên hiệu ứng ngày lương của K0 có thể bắt đầu trước ngày đến hạn.
                t.Date.Should().BeOnOrAfter(firstEvent.AddDays(-3));
            }
        }

        (diff.Added.Count + diff.Removed.Count + diff.Changed.Count).Should().BeGreaterThan(0);
        k2.EventDays.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K2_PaysTheMainIncomeLate_InMonthM_AndChangesItsAmountFromMonthMPlusTwo(string persona, int seed)
    {
        if (persona is Persona.Freelancer or Persona.SmallTrader)
        {
            return;   // không có khoản thu định kỳ; phần dời/gộp đã có ở test trên
        }

        var k0 = WorldBuilder.Build(persona, seed, ScenarioCodes.K0);
        var k2 = WorldBuilder.Build(persona, seed, ScenarioCodes.K2);
        var changed = k0.Recurring.Zip(k2.Recurring).Where(p => !p.First.Occurrences.SequenceEqual(p.Second.Occurrences)).ToList();

        changed.Should().ContainSingle();
        var (before, after) = changed[0];
        before.Direction.Should().Be(RecurringDirection.Inflow);
        var pairs = before.Occurrences.Zip(after.Occurrences).Where(p => p.First != p.Second).ToList();
        var late = pairs.First();
        (late.Second.PaidOn!.Value.DayNumber - late.First.PaidOn!.Value.DayNumber).Should().BeInRange(3, 10);
        var rescaled = pairs.Skip(1).ToList();
        rescaled.Should().NotBeEmpty();
        rescaled.Should().OnlyContain(p => p.First.Due >= late.First.Due.AddMonths(2));
        var ratio = (double)rescaled[0].Second.NominalCents / rescaled[0].First.NominalCents;
        Math.Abs(ratio - 1).Should().BeInRange(0.09, 0.31);
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K3_SkipsOnePeriodOfOneOrTwoBills_AndOptionallyPaysBothNextMonth(string persona, int seed)
    {
        var (k0, k3, diff) = Compare(persona, seed, ScenarioCodes.K3);

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().HaveCountGreaterThanOrEqualTo(1).And.HaveCountLessThanOrEqualTo(2);
        diff.Removed.Should().OnlyContain(t => t.Label == TxLabel.Recurring && t.Type == TransactionType.Debit);
        foreach (var (before, after) in diff.Changed)
        {
            after.RecurringName.Should().Be(before.RecurringName);
            diff.Removed.Should().Contain(r => r.RecurringName == before.RecurringName);
            after.AmountCents.Should().BeGreaterThan(before.AmountCents, "kỳ sau trả gộp hai kỳ");
        }

        k3.EventDays.Should().HaveCount(diff.Removed.Count);
        k3.Recurring.SelectMany(r => r.Occurrences).Count(o => o.TransactionId is null)
            .Should().Be(k0.Recurring.SelectMany(r => r.Occurrences).Count(o => o.TransactionId is null) + diff.Removed.Count);
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K4_RaisesOneSmallBillByFivePercent_AndOneFixedCostByTwentyFive(string persona, int seed)
    {
        var (_, k4, diff) = Compare(persona, seed, ScenarioCodes.K4);

        diff.Added.Should().BeEmpty();
        diff.Removed.Should().BeEmpty();
        var byItem = diff.Changed.GroupBy(c => c.Before.RecurringName!).ToDictionary(g => g.Key, g => g.ToList());
        byItem.Should().HaveCount(2);
        var ratios = byItem.Values.Select(changes => (double)changes[0].After.AmountCents / changes[0].Before.AmountCents).Order().ToList();
        ratios[0].Should().BeApproximately(1.05, 0.01);
        ratios[1].Should().BeApproximately(1.25, 0.01);
        diff.Changed.Should().OnlyContain(c => c.After.At == c.Before.At);
        k4.EventDays.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K5_ChangesSpendingOnlyAroundTet_AndK5hHasTheSameData(string persona, int seed)
    {
        var (_, k5, diff) = Compare(persona, seed, ScenarioCodes.K5);
        var from = GeneratorSpec.Tet2026.AddDays(-14);
        var to = GeneratorSpec.Tet2026.AddDays(7);

        foreach (var t in diff.Removed.Concat(diff.Added).Concat(diff.Changed.Select(c => c.After)))
        {
            if (persona == Persona.SmallTrader && t.Merchant == "Nộp tiền")
            {
                continue;   // nộp tiền mặt phụ thuộc số dư tiền mặt, nên lệch dần sau Tết
            }

            if (t.Label == TxLabel.Refund)
            {
                t.Date.Should().BeOnOrBefore(to.AddDays(10));   // hoàn tiền của khoản mua trong dịp Tết về tới 10 ngày sau
                continue;
            }

            t.Date.Should().BeOnOrAfter(from).And.BeOnOrBefore(to);
        }

        var flexK5 = k5.Transactions.Count(t => t.Label == TxLabel.Flexible && t.Date >= from && t.Date <= to);
        var flexK0 = WorldBuilder.Build(persona, seed, ScenarioCodes.K0).Transactions.Count(t => t.Label == TxLabel.Flexible && t.Date >= from && t.Date <= to);
        flexK5.Should().BeGreaterThan(flexK0);
        k5.EventDays.Should().HaveCount(22);
        WorldBuilder.Build(persona, seed, ScenarioCodes.K5h).Transactions.Should().Equal(k5.Transactions);
    }

    [Theory]
    [MemberData(nameof(PersonaSeeds))]
    public void K6_DropsHistoryBeforeTheStartDay_AndCarriesTheBalanceOver(string persona, int seed)
    {
        var k0 = WorldBuilder.Build(persona, seed, ScenarioCodes.K0);
        var k6 = WorldBuilder.Build(persona, seed, ScenarioCodes.K6);
        var start = k6.FirstDay;

        start.Should().BeOnOrAfter(new DateOnly(2025, 7, 15)).And.BeOnOrBefore(new DateOnly(2026, 4, 15));
        k6.Transactions.Should().Equal(k0.Transactions.Where(t => t.Date >= start));
        foreach (var account in k6.Accounts)
        {
            var original = k0.Accounts.Single(a => a.Id == account.Id);
            account.OpeningBalanceCents.Should().Be(original.OpeningBalanceCents + BalanceEffects.Sum(k0.Transactions.Where(t => t.Date < start), account.Id));
        }

        k6.Loans.Where(l => l.Start < start).Should().BeEmpty();
    }

    private static (SynthWorld K0, SynthWorld Other, Diff Diff) Compare(string persona, int seed, string scenario)
    {
        var k0 = WorldBuilder.Build(persona, seed, ScenarioCodes.K0);
        var other = WorldBuilder.Build(persona, seed, scenario);
        var before = k0.Transactions.ToDictionary(t => t.Id);
        var after = other.Transactions.ToDictionary(t => t.Id);
        var diff = new Diff(
            other.Transactions.Where(t => !before.ContainsKey(t.Id)).ToList(),
            k0.Transactions.Where(t => !after.ContainsKey(t.Id)).ToList(),
            k0.Transactions.Where(t => after.TryGetValue(t.Id, out var a) && a != t).Select(t => (t, after[t.Id])).ToList());
        return (k0, other, diff);
    }

    private sealed record Diff(
        IReadOnlyList<SynthTransaction> Added,
        IReadOnlyList<SynthTransaction> Removed,
        IReadOnlyList<(SynthTransaction Before, SynthTransaction After)> Changed);
}
