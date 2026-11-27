using FinMate.Backtest;
using FinMate.Backtest.Comparison;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

/// <summary>Phase 97, Task 11: <c>--compare-runs</c> chỉ đọc CSV đã có và chép số nguyên văn; ngưỡng "khác đáng kể" là khoảng tin cậy của tập kiểm tra.</summary>
public sealed class CompareRunsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "compare-runs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Một lượt chạy giả: mọi hồ sơ × tầm cùng một số, đủ để kiểm tra chép số và cờ.</summary>
    private void WriteRun(string name, long mae, long bias, double coverage, (double Low, double High) coverageCi, double holdImprovement, (double Low, double High) holdCi, string? method, bool income)
    {
        var dir = Path.Combine(_root, name, "forecast-backtest");
        Directory.CreateDirectory(dir);
        var balance = new List<string> { "mode,scenario,persona,confidence,horizon,window,method,n,users,mae_vnd,median_ae_vnd,bias_vnd,scale_rel_pct,model_improvement_pct,ci_low_pct,ci_high_pct" };
        var band = new List<string> { "mode,scenario,persona,confidence,horizon,window,n,users,coverage_pct,ci_low_pct,ci_high_pct,mean_width_vnd,median_width_vnd" };
        var incomeRows = new List<string> { "mode,scenario,persona,confidence,horizon,window,n,users,wape_pct,ci_low_pct,ci_high_pct,bias_pct" };
        foreach (var p in CompareRuns.Personas)
        {
            foreach (var h in CompareRuns.Horizons)
            {
                balance.Add($"oracle,K0,{p},all,{h},all,model,100,10,{mae},1,{bias},50.0,,,");
                balance.AddRange(CompareRuns.Baselines.Select(b => $"oracle,K0,{p},all,{h},all,{b},100,10,1,1,1,100.0,{holdImprovement:0.0},{holdCi.Low:0.0},{holdCi.High:0.0}"));
                band.Add($"oracle,K0,{p},all,{h},all,100,10,{coverage:0.0},{coverageCi.Low:0.0},{coverageCi.High:0.0},2500000,2000000");
                foreach (var (s, w) in new[] { ("K0", "all"), ("K2", "all"), ("K2", "affected") })
                {
                    incomeRows.Add($"oracle,{s},{p},all,{h},{w},100,10,40.0,30.0,50.0,-1.5");
                }
            }
        }

        File.WriteAllLines(Path.Combine(dir, "balance.csv"), balance);
        File.WriteAllLines(Path.Combine(dir, "band.csv"), band);
        if (income)
        {
            File.WriteAllLines(Path.Combine(dir, "income.csv"), incomeRows);
        }

        File.WriteAllText(Path.Combine(_root, name, "forecast-backtest.md"), method is null ? "# Backtest dự báo số dư\n" : $"# Backtest dự báo số dư — mô hình `{method}`\n");
    }

    private void WriteAll(double testCoverage)
    {
        WriteRun("dev-before", 9_000_000, -7_000_000, 35.0, (33, 37), -20.0, (-30, -10), null, income: false);
        WriteRun("dev-after", 4_500_000, -200_000, 46.0, (44, 48), 36.0, (30, 42), "mean_dow_income_sumband_v3", income: true);
        WriteRun("test-before", 9_100_000, -7_100_000, 34.0, (32, 36), -21.0, (-31, -11), null, income: false);
        WriteRun("test-after", 4_600_000, -100_000, testCoverage, (testCoverage - 2, testCoverage + 2), 35.0, (29, 41), "mean_dow_income_sumband_v3", income: true);
    }

    [Fact]
    public void TheReport_CopiesTheNumbersFromTheCsv_AndAppendsTheNotesVerbatim()
    {
        WriteAll(testCoverage: 45.0);
        var notes = Path.Combine(_root, "notes.md");
        File.WriteAllText(notes, "## Điểm yếu còn lại\n\nGhi chú nguyên văn.\n");

        var md = CompareRuns.Run(_root, notes);

        md.Should().Contain("| all | 14 | 9,00 → 4,50 | 50,0% | −7,00 → −0,20 | 35,0% → 46,0% | 2,50 → 2,50 |");
        md.Should().Contain("`dev-before` (trước Phase 97)").And.Contain("`test-after` (mean_dow_income_sumband_v3)");
        md.Should().Contain("| K2 | mọi mốc | student | 40,0% / −1,5% | 40,0% / −1,5% |");
        md.Should().Contain("Số ô khác đáng kể: 0 / 18.");
        md.TrimEnd().Should().EndWith("Ghi chú nguyên văn.");
    }

    [Fact]
    public void ADevNumberOutsideTheTestConfidenceInterval_IsFlaggedAsADifference()
    {
        WriteAll(testCoverage: 40.0);   // dev 46% nằm ngoài CI test 38–42%

        var md = CompareRuns.Run(_root, null);

        md.Should().Contain("**có**: trong dải").And.Contain("Số ô khác đáng kể: 18 / 18.");
    }

    [Fact]
    public void AMissingRow_FailsInsteadOfGuessing()
    {
        WriteAll(testCoverage: 45.0);
        var band = Path.Combine(_root, "test-after", "forecast-backtest", "band.csv");
        File.WriteAllLines(band, File.ReadAllLines(band).Where(l => !l.Contains(",family,all,30,")));

        FluentActions.Invoking(() => CompareRuns.Run(_root, null)).Should().Throw<InvalidDataException>().WithMessage("*K0/family/30/all*");
    }

    [Fact]
    public void Parse_AcceptsTheCompareOptions()
    {
        var args = new[] { "--compare-runs", "root", "--notes", "n.md", "--out", "r.md" };

        FluentActions.Invoking(() => Program.Parse(args)).Should().NotThrow();
        Program.OptionValue(args, "--compare-runs").Should().Be("root");
        Program.OptionValue(args, "--notes").Should().Be("n.md");
    }
}
