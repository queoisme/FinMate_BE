using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

public class WorldBuilderTests
{
    public static TheoryData<string> AllPersonas => new(GeneratorSpec.Personas);

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void SameInputs_GiveTheSameWorld_ToTheLastDong(string persona)
    {
        var a = WorldBuilder.Build(persona, 3, ScenarioCodes.K0);
        var b = WorldBuilder.Build(persona, 3, ScenarioCodes.K0);

        b.Transactions.Should().Equal(a.Transactions);
        b.Accounts.Should().Equal(a.Accounts);
        b.Recurring.SelectMany(r => r.Occurrences).Should().Equal(a.Recurring.SelectMany(r => r.Occurrences));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentPeople_WithTheSameUserIdAcrossScenarios()
    {
        var a = WorldBuilder.Build(Persona.StableWorker, 1, ScenarioCodes.K0);
        var b = WorldBuilder.Build(Persona.StableWorker, 2, ScenarioCodes.K0);

        a.UserId.Should().NotBe(b.UserId);
        a.Transactions.Sum(t => t.AmountCents).Should().NotBe(b.Transactions.Sum(t => t.AmountCents));
        WorldBuilder.Build(Persona.StableWorker, 1, ScenarioCodes.K3).UserId.Should().Be(a.UserId);
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void Transactions_StayInsideTheDataWindow_WithUniqueIds_AndWholeThousands(string persona)
    {
        var world = WorldBuilder.Build(persona, 0, ScenarioCodes.K0);

        world.Transactions.Should().OnlyContain(t => t.Date >= GeneratorSpec.DataStart && t.Date <= GeneratorSpec.DataEnd);
        world.Transactions.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        world.Transactions.Should().OnlyContain(t => t.AmountCents > 0 && t.AmountCents % Rng.MoneyStep == 0);
        world.Transactions.Should().BeInAscendingOrder(t => t.At);
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void TransactionKinds_MatchWhatProductionStores(string persona)
    {
        var world = WorldBuilder.Build(persona, 0, ScenarioCodes.K0);
        var accountIds = world.Accounts.Select(a => a.Id).ToHashSet();

        world.Transactions.Should().OnlyContain(t => accountIds.Contains(t.AccountId));
        foreach (var t in world.Transactions)
        {
            switch (t.Label)
            {
                case TxLabel.Transfer:
                    t.Type.Should().Be(TransactionType.Transfer);
                    accountIds.Should().Contain(t.CounterAccountId!.Value);
                    break;
                case TxLabel.Fund:
                    t.Type.Should().Be(TransactionType.Transfer);
                    t.CounterAccountId.Should().BeNull();
                    t.FundName.Should().NotBeNullOrEmpty();
                    break;
                case TxLabel.Loan:
                    t.CategorySlug.Should().Be(Slugs.Loan);
                    break;
                case TxLabel.Refund:
                    t.Relation.Should().Be(TransactionRelation.Refund);
                    break;
                default:
                    t.Type.Should().NotBe(TransactionType.Transfer);
                    t.CategorySlug.Should().NotBeNull();
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void Refunds_PointAtAnEarlierShoppingDebit_AndNeverExceedIt(string persona)
    {
        var world = WorldBuilder.Build(persona, 5, ScenarioCodes.K0);
        var byId = world.Transactions.ToDictionary(t => t.Id);

        foreach (var refund in world.Transactions.Where(t => t.Label == TxLabel.Refund))
        {
            var original = byId[refund.RelatedTransactionId!.Value];
            original.Type.Should().Be(TransactionType.Debit);
            original.CategorySlug.Should().Be(Slugs.Shopping);
            refund.AmountCents.Should().BeLessThanOrEqualTo(original.AmountCents);
            refund.At.Should().BeAfter(original.At);
        }
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void TruthOccurrences_PointAtRealTransactions(string persona)
    {
        var world = WorldBuilder.Build(persona, 2, ScenarioCodes.K0);
        var byId = world.Transactions.ToDictionary(t => t.Id);

        foreach (var item in world.Recurring)
        {
            foreach (var o in item.Occurrences.Where(o => o.TransactionId is not null))
            {
                var tx = byId[o.TransactionId!.Value];
                tx.RecurringName.Should().Be(item.Name);
                tx.AmountCents.Should().Be(o.PaidCents);
                tx.Date.Should().Be(o.PaidOn);
                CounterpartyKey.From(tx.Merchant).Should().Be(CounterpartyKey.From(item.Name));
            }
        }
    }

    [Fact]
    public void MerchantVariants_AllShareTheCanonicalCounterpartyKey()
    {
        var world = WorldBuilder.Build(Persona.StableWorker, 0, ScenarioCodes.K0);
        var rent = world.Transactions.Where(t => t.RecurringName == "Tiền nhà").Select(t => t.Merchant).ToList();

        rent.Distinct().Count().Should().BeGreaterThan(1, "20% số lần viết khác kiểu");
        rent.Select(CounterpartyKey.From).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void SalaryIsPaidOnAWeekday_AndRentIsMonthly()
    {
        var world = WorldBuilder.Build(Persona.StableWorker, 4, ScenarioCodes.K0);
        var salary = world.Recurring.Single(r => r.Name == "Lương công ty");

        salary.Occurrences.Where(o => o.PaidOn is not null)
            .Should().OnlyContain(o => o.PaidOn!.Value.DayOfWeek != DayOfWeek.Saturday && o.PaidOn.Value.DayOfWeek != DayOfWeek.Sunday);
        world.Recurring.Single(r => r.Name == "Tiền nhà").Occurrences.Should().HaveCount(19);   // 2025-01 → 2026-07
    }

    [Fact]
    public void FlexibleSpending_IsCloseToTheNominalMonthlyBudget()
    {
        foreach (var seed in Enumerable.Range(0, 5))
        {
            var profile = WorldBuilder.ProfileOf(Persona.StableWorker, seed);
            var world = WorldBuilder.Build(Persona.StableWorker, seed, ScenarioCodes.K0);
            var firstQuarter = world.Transactions
                .Where(t => t.Label == TxLabel.Flexible && t.Date < new DateOnly(2025, 4, 1))
                .Sum(t => t.AmountCents) / 3.0;

            firstQuarter.Should().BeInRange(profile.FlexMonthlyCents * 0.75, profile.FlexMonthlyCents * 1.4);
        }
    }

    [Fact]
    public void Family_HasLoansFundsSavingsTransfers_AndAnUncountedSavingsAccount()
    {
        var world = WorldBuilder.Build(Persona.Family, 0, ScenarioCodes.K0);

        world.Accounts.Should().Contain(a => !a.IncludeInForecast);
        world.Transactions.Should().Contain(t => t.Label == TxLabel.Fund);
        world.Loans.Should().HaveCount(2);
        var borrow = world.Loans.Single(l => l.Direction == LoanDirection.Borrow);
        borrow.Repayments.Sum(r => r.AmountCents).Should().Be(borrow.PrincipalCents);
        world.Transactions.Where(t => t.Label == TxLabel.Loan).Should().HaveCountGreaterThan(4);
    }

    [Fact]
    public void SmallTrader_DepositsCash_AndHasAVolatileWeeklyStockPurchase()
    {
        var world = WorldBuilder.Build(Persona.SmallTrader, 0, ScenarioCodes.K0);

        world.Transactions.Should().Contain(t => t.Merchant == "Nộp tiền" && t.Type == TransactionType.Transfer);
        var stock = world.Recurring.Single(r => r.Name == "Đại lý");
        stock.Volatile.Should().BeTrue();
        stock.Cadence.Should().Be(RecurringCadence.Weekly);
    }

    [Fact]
    public void LowBalanceThreshold_IsTwentyPercentOfMonthlyIncome()
    {
        var world = WorldBuilder.Build(Persona.Student, 0, ScenarioCodes.K0);

        world.LowBalanceThresholdCents.Should().Be(Rng.RoundTo(world.MonthlyIncomeCents * 0.2, 100_000));
    }
}
