using System.Diagnostics;
using System.Globalization;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;
using FinMate.Backtest.Reporting;

namespace FinMate.Backtest;

/// <summary>
/// Backtest dự báo (Phase 96). <c>dotnet run --project FinMate.Backtest -c Release -- [--out DIR] [--seed N] [--seeds N] [--step N] [--quick]</c>.
/// Ghi <c>DIR/forecast-backtest.md</c> và <c>DIR/forecast-backtest/*.csv</c>. Chỉ in tiến độ, không in số tiền (D21).
/// </summary>
public static class Program
{
    public const string ReportName = "forecast-backtest";

    public static int Main(string[] args)
    {
        try
        {
            var (options, outDir) = Parse(args);
            var watch = Stopwatch.StartNew();
            if (DiagnosePath(args) is { } diagnose)
            {
                File.WriteAllText(diagnose, Diagnostics.Diagnosis.Run(options, line => Console.Error.WriteLine(line)));
                Console.Error.WriteLine($"Chẩn đoán xong trong {watch.Elapsed.TotalSeconds:0.0} s: {diagnose}");
                return 0;
            }

            var (markdown, csv) = Produce(options, line => Console.Error.WriteLine(line));
            Write(outDir, markdown, csv);
            Console.Error.WriteLine($"Xong trong {watch.Elapsed.TotalSeconds:0.0} s. Báo cáo: {Path.Combine(outDir, ReportName + ".md")}");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Tham số sai: {ex.Message}");
            return 2;
        }
    }

    /// <summary>Chạy toàn bộ và trả nội dung báo cáo (không ghi đĩa), để test so từng byte.</summary>
    public static (string Markdown, IReadOnlyDictionary<string, string> Csv) Produce(RunOptions options, Action<string>? progress = null)
    {
        var rows = BacktestRunner.Run(options, progress);
        var metrics = MetricsBook.Compute(rows);
        var worlds = options.Personas.Count * options.SeedsPerPersona * options.Scenarios.Count;
        var info = RunInfoFactory.Create(options, worlds, rows.Count);
        return (ReportWriter.Markdown(info, metrics), ReportWriter.Csv(metrics));
    }

    public static (RunOptions Options, string OutDir) Parse(IReadOnlyList<string> args)
    {
        var options = RunOptions.Default;
        var outDir = Path.Combine("..", "docs", "evaluation");
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--out": outDir = Value(args, ++i); break;
                case "--seed": options = options with { RootSeed = ulong.Parse(Value(args, ++i), CultureInfo.InvariantCulture) }; break;
                case "--seeds": options = options with { SeedsPerPersona = Positive(Value(args, ++i)) }; break;
                case "--step": options = options with { StepDays = Positive(Value(args, ++i)) }; break;
                case "--quick": options = options with { SeedsPerPersona = 2, StepDays = 7 }; break;
                case "--diagnose": _ = Value(args, ++i); break;   // chẩn đoán sau tầng 1; không ghi báo cáo backtest
                default: throw new ArgumentException($"không hiểu '{args[i]}'");
            }
        }

        return (options, outDir);
    }

    /// <summary><c>--diagnose FILE</c>: chạy chẩn đoán (Phase 96, sau tầng 1) và ghi Markdown vào FILE thay cho báo cáo backtest.</summary>
    public static string? DiagnosePath(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--diagnose")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void Write(string outDir, string markdown, IReadOnlyDictionary<string, string> csv)
    {
        var csvDir = Path.Combine(outDir, ReportName);
        Directory.CreateDirectory(csvDir);
        File.WriteAllText(Path.Combine(outDir, ReportName + ".md"), markdown);
        foreach (var (name, content) in csv)
        {
            File.WriteAllText(Path.Combine(csvDir, name), content);
        }
    }

    private static string Value(IReadOnlyList<string> args, int i) => i < args.Count ? args[i] : throw new ArgumentException($"thiếu giá trị sau {args[i - 1]}");

    private static int Positive(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new ArgumentException($"'{text}' phải là số nguyên dương");
}
