using FinMate.Backtest;
using FinMate.Backtest.Diagnostics;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

/// <summary>Chẩn đoán sau tầng 1 chỉ có giá trị khi hai bất biến này đúng: thành phần cộng lại đúng sai số số dư, và (a) dựng lại đúng production.</summary>
public class DiagnosisTests
{
    [Fact]
    public void ComponentErrors_SumToTheBalanceError_AndVariantA_ReplicatesProduction()
    {
        var text = Diagnosis.Run(RunOptions.Default with { SeedsPerPersona = 1, StepDays = 30 });

        text.Should().Contain("ở 0 dòng (phải là 0)");
        var line = text.Split('\n').Single(l => l.StartsWith("Dựng lại (a) khớp production ở ", StringComparison.Ordinal));
        var counts = line["Dựng lại (a) khớp production ở ".Length..].Split(' ')[0].Split('/');
        counts[0].Should().Be(counts[1]);
        text.Should().Contain("ngoài khoảng, bằng đúng K5");
    }

    [Fact]
    public void ActualComponents_CoverEveryRawAndBoundaryRow()
    {
        var world = WorldBuilder.Build(Persona.Family, 0, ScenarioCodes.K0);
        var harness = new ForecastHarness(world, new());
        var (from, to) = (new DateOnly(2025, 9, 1), new DateOnly(2025, 12, 31));
        var flows = Components.Actual(harness.TruthRaw(from, to), harness.TruthBoundary(from, to), world.Transactions.ToDictionary(t => t.Id));

        flows.Total.Should().Be(harness.ActualBalance(0, from, to));
        flows[Component.LoanTransfer].Should().NotBe(0);
        flows[Component.RecurringOutflow].Should().BeNegative();
    }

    [Fact]
    public void DiagnoseFlag_IsReadWithoutChangingTheBacktestOptions()
    {
        Program.DiagnosePath(["--quick", "--diagnose", "x.md"]).Should().Be("x.md");
        Program.DiagnosePath(["--quick"]).Should().BeNull();
        Program.Parse(["--diagnose", "x.md"]).Options.Should().Be(RunOptions.Default);
    }
}
