using FinMate.Backtest;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Reporting;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

public class BacktestReportTests
{
    private static readonly RunOptions Tiny = RunOptions.Default with { SeedsPerPersona = 1, StepDays = 45 };

    [Fact]
    public void TwoRunsWithTheSameInputs_ProduceTheSameFiles_ByteForByte()
    {
        var first = Program.Produce(Tiny);
        var second = Program.Produce(Tiny);

        second.Markdown.Should().Be(first.Markdown);
        second.Csv.Keys.Should().Equal(first.Csv.Keys);
        foreach (var (name, content) in first.Csv)
        {
            second.Csv[name].Should().Be(content, name);
        }
    }

    [Fact]
    public void ADifferentRootSeed_ChangesTheNumbers()
    {
        var a = Program.Produce(Tiny).Csv["balance.csv"];
        var b = Program.Produce(Tiny with { RootSeed = 42 }).Csv["balance.csv"];

        b.Should().NotBe(a);
    }

    [Fact]
    public void TheReport_StartsWithReproducibilityAndALaySummary_AndNamesTheModeOnEverySummaryLine()
    {
        var (markdown, csv) = Program.Produce(Tiny);

        markdown.Should().Contain("## Tái lập").And.Contain($"v{GeneratorSpec.Version}").And.Contain("SHA-256");
        markdown.IndexOf("## Tóm tắt", StringComparison.Ordinal).Should().BeLessThan(markdown.IndexOf("## 1.", StringComparison.Ordinal));
        var summary = markdown[markdown.IndexOf("## Tóm tắt", StringComparison.Ordinal)..markdown.IndexOf("## 1.", StringComparison.Ordinal)];
        summary.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| #", StringComparison.Ordinal))
            .Should().NotBeEmpty().And.OnlyContain(l => l.Contains("chế độ đáp án"));
        csv.Keys.Should().BeEquivalentTo(["balance.csv", "band.csv", "flexible.csv", "effects.csv"]);
        csv["balance.csv"].Split('\n')[0].Should().StartWith("mode,scenario,persona,confidence,horizon,window,method");
    }

    [Fact]
    public void RatesWithFewerThanTwentySamples_AreMarkedNotEnough()
    {
        Format.PctCi(55.5, 50, 60, 19).Should().Be(Format.NotEnough);
        Format.PctCi(55.5, 50, 60, 20).Should().Be("55,5% (CI 95%: 50,0% – 60,0%)");
        Format.Money(-1_234_567).Should().Be("−1.234.567 đ");
        Format.Money(999).Should().Be("999 đ");
    }

    [Fact]
    public void K6Origins_AreFourteenToSeventyFiveDaysAfterTheStart_AndK5hAddsTheTetHoliday()
    {
        var k6 = WorldBuilder.Build(Persona.Student, 0, ScenarioCodes.K6);
        var origins = BacktestRunner.Origins(k6, 1).ToList();

        origins.Should().OnlyContain(o => o >= k6.FirstDay.AddDays(14) && o <= k6.FirstDay.AddDays(75));
        BacktestRunner.SettingsFor(ScenarioCodes.K5h, new()).Holidays.Should().ContainSingle(h => h.MultiplierPercent == 150 && h.Start == new DateOnly(2026, 2, 10));
        BacktestRunner.SettingsFor(ScenarioCodes.K5, new()).Holidays.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReadsTheOptions_AndRejectsUnknownOnes()
    {
        var (options, outDir) = Program.Parse(["--seeds", "3", "--step", "2", "--seed", "7", "--out", "x"]);

        (options.SeedsPerPersona, options.StepDays, options.RootSeed, outDir).Should().Be((3, 2, 7UL, "x"));
        Program.Parse(["--quick"]).Options.Should().Match<RunOptions>(o => o.SeedsPerPersona == 2 && o.StepDays == 7);
        FluentActions.Invoking(() => Program.Parse(["--bogus"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Program.Parse(["--step", "0"])).Should().Throw<ArgumentException>();
    }
}
