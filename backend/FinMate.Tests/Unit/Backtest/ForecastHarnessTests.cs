using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

public class ForecastHarnessTests
{
    private static readonly DateOnly Origin = new(2025, 10, 15);

    public static TheoryData<string> AllPersonas => new(GeneratorSpec.Personas);

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void AddingOrChangingTransactionsFromTheOriginOn_DoesNotChangeTheForecastAtTheOrigin(string persona)
    {
        var world = WorldBuilder.Build(persona, 1, ScenarioCodes.K0);
        var before = Json(new ForecastHarness(world, new ForecastSettings()).ForecastAt(Origin, 30));

        var bank = world.Accounts[0].Id;
        var originAt = GeneratorSpec.At(Origin, 0);
        var future = world.Transactions
            .Select(t => t.At >= originAt ? t with { AmountCents = t.AmountCents * 3 } : t)
            .Append(new SynthTransaction(Guid.NewGuid(), bank, TransactionType.Debit, 50_000_000, originAt, "Tương lai", Slugs.Other, TxLabel.Flexible))
            .Append(new SynthTransaction(Guid.NewGuid(), bank, TransactionType.Credit, 9_000_000, originAt.AddDays(1), "Tương lai", Slugs.Income, TxLabel.Flexible))
            .OrderBy(t => t.At).ThenBy(t => t.Id)
            .ToList();
        var after = Json(new ForecastHarness(world with { Transactions = future }, new ForecastSettings()).ForecastAt(Origin, 30));

        after.Should().Be(before);
    }

    [Fact]
    public void ATransactionOneTickBeforeTheOrigin_DoesChangeTheForecast()
    {
        // Đối chứng cho test trên: lọc theo thời điểm thật sự nhìn thấy giao dịch ngay trước mốc.
        var world = WorldBuilder.Build(Persona.StableWorker, 1, ScenarioCodes.K0);
        var before = new ForecastHarness(world, new ForecastSettings()).ForecastAt(Origin, 30);
        var justBefore = GeneratorSpec.At(Origin, 0).AddTicks(-1);
        var extra = new SynthTransaction(Guid.NewGuid(), world.Accounts[0].Id, TransactionType.Debit, 1_000_000, justBefore, "Vừa xong", Slugs.Other, TxLabel.Flexible);
        var after = new ForecastHarness(world with { Transactions = world.Transactions.Append(extra).OrderBy(t => t.At).ToList() }, new ForecastSettings())
            .ForecastAt(Origin, 30);

        after.StartBalanceCents.Should().Be(before.StartBalanceCents - 1_000_000);
    }

    [Fact]
    public void AViewAtEightInTheMorning_SeesOnlyTransactionsBeforeEight()
    {
        var day = new DateOnly(2025, 9, 9);
        var world = WorldBuilder.Build(Persona.Student, 0, ScenarioCodes.K0);
        var bank = world.Accounts[0].Id;
        var early = new SynthTransaction(Guid.NewGuid(), bank, TransactionType.Debit, 10_000, GeneratorSpec.At(day, 7 * 60 + 59), "Sớm", Slugs.Food, TxLabel.Flexible);
        var late = new SynthTransaction(Guid.NewGuid(), bank, TransactionType.Debit, 20_000, GeneratorSpec.At(day, 8 * 60), "Muộn", Slugs.Food, TxLabel.Flexible);
        var transactions = world.Transactions.Where(t => t.Date != day).Append(early).Append(late).OrderBy(t => t.At).ThenBy(t => t.Id).ToList();
        var clock = new BacktestClock { Now = GeneratorSpec.At(day, 8 * 60) };
        var view = new WorldView(world with { Transactions = transactions }, clock);

        view.Raw(day, day).Select(t => t.Id).Should().Equal(early.Id);
        clock.Now = GeneratorSpec.At(day, 8 * 60 + 1);
        view.Raw(day, day).Select(t => t.Id).Should().Equal(early.Id, late.Id);
    }

    [Theory]
    [MemberData(nameof(AllPersonas))]
    public void StartBalancePlusActualFlow_EqualsTheRebuiltBalanceAtTheHorizon(string persona)
    {
        foreach (var scenario in new[] { ScenarioCodes.K0, ScenarioCodes.K6 })
        {
            var world = WorldBuilder.Build(persona, 2, scenario);
            var harness = new ForecastHarness(world, new ForecastSettings());
            var origin = world.FirstDay > Origin ? world.FirstDay.AddDays(20) : Origin;
            var forecast = harness.ForecastAt(origin, 30);

            forecast.StartBalanceCents.Should().Be(harness.CountedBalanceNow());
            foreach (var h in new[] { 7, 14, 30 })
            {
                var actual = harness.ActualBalance(forecast.StartBalanceCents, origin, origin.AddDays(h));
                harness.Clock.SetOrigin(origin.AddDays(h + 1));
                actual.Should().Be(harness.CountedBalanceNow(), $"{persona} {scenario} h={h}");
                harness.Clock.SetOrigin(origin);
            }
        }
    }

    [Fact]
    public void TheForecastRunsThroughTheRealService_WithLoansAndBoundaryTransfers()
    {
        var world = WorldBuilder.Build(Persona.Family, 0, ScenarioCodes.K0);
        var forecast = new ForecastHarness(world, new ForecastSettings()).ForecastAt(new DateOnly(2025, 12, 1), 30);

        forecast.Days.Should().HaveCount(31);
        forecast.Days.SelectMany(d => d.Events).Should().Contain(e => e.Kind == CashflowEventKinds.LoanOutflow);
        forecast.Days.SelectMany(d => d.Events).Should().Contain(e => e.Label == "Gửi tiết kiệm" && e.AmountCents < 0);
        forecast.Days.SelectMany(d => d.Events).Should().Contain(e => e.Label == "Quỹ gia đình" && e.AmountCents < 0);
        forecast.Confidence.Should().Be("normal");
    }

    [Fact]
    public void OracleItems_UseOnlyPaymentsBeforeNow()
    {
        var world = WorldBuilder.Build(Persona.StableWorker, 3, ScenarioCodes.K0);
        var rent = world.Recurring.Single(r => r.Name == "Tiền nhà");
        var paid = rent.Occurrences.First(o => o.PaidOn > Origin);
        var justBeforePayment = world.Transactions.Single(t => t.Id == paid.TransactionId).At;

        var before = OracleRecurring.ItemsAt(world, justBeforePayment).Single(i => i.Name == "Tiền nhà");
        var after = OracleRecurring.ItemsAt(world, justBeforePayment.AddTicks(1)).Single(i => i.Name == "Tiền nhà");

        before.NextDueDate.Should().Be(paid.Due);
        after.NextDueDate.Should().BeAfter(paid.Due);
        OracleRecurring.ItemsAt(world, justBeforePayment).Should().NotContain(i => i.Name == "Đại lý");
    }

    [Fact]
    public void OracleItems_ExcludeVolatileItems()
    {
        var world = WorldBuilder.Build(Persona.SmallTrader, 0, ScenarioCodes.K0);

        OracleRecurring.ItemsAt(world, GeneratorSpec.At(Origin, 0)).Should().NotContain(i => i.Name == "Đại lý")
            .And.Contain(i => i.Name == "Thuê sạp");
    }

    [Fact]
    public void AnUnexpectedRepositoryCall_FailsLoudly()
    {
        var view = new WorldView(WorldBuilder.Build(Persona.Student, 0, ScenarioCodes.K0), new BacktestClock());

        var act = () => new FakeAccounts(view).AddAsync(new FinancialAccount());

        act.Should().ThrowAsync<NotSupportedException>().WithMessage("backtest: AddAsync*");
    }

    private static string Json(CashflowForecastDto forecast) => JsonSerializer.Serialize(forecast);
}
