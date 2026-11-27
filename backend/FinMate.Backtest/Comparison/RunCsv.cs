using System.Globalization;

namespace FinMate.Backtest.Comparison;

/// <summary>CSV của một lượt chạy backtest (chế độ đáp án, mọi mức tin cậy). Thiếu dòng nào thì báo lỗi, không đoán.</summary>
public sealed class RunCsv
{
    public sealed record ModelRow(long Mae, long Bias);

    public sealed record BaselineRow(double? Improvement, double? CiLow, double? CiHigh);

    public sealed record BandRow(double? Coverage, double? CiLow, double? CiHigh, long Width);

    public sealed record IncomeRow(double? Wape, double? Bias);

    private readonly List<Dictionary<string, string>> _balance;
    private readonly List<Dictionary<string, string>> _band;
    private readonly List<Dictionary<string, string>>? _income;
    private readonly string _dir;

    private RunCsv(string dir, List<Dictionary<string, string>> balance, List<Dictionary<string, string>> band, List<Dictionary<string, string>>? income, string method)
        => (_dir, _balance, _band, _income, Method) = (dir, balance, band, income, method);

    /// <summary>Tên phương pháp lấy từ dòng đầu của báo cáo Markdown cạnh thư mục CSV (từ Phase 97); không có thì "trước Phase 97".</summary>
    public string Method { get; }

    public static RunCsv Load(string csvDir)
    {
        var report = csvDir + ".md";
        var first = File.Exists(report) ? File.ReadLines(report).First() : string.Empty;
        var tick = first.IndexOf('`');
        var method = tick >= 0 && first.IndexOf('`', tick + 1) is var end and > 0 ? first[(tick + 1)..end] : "trước Phase 97";
        var income = Path.Combine(csvDir, "income.csv");
        return new RunCsv(csvDir, Read(Path.Combine(csvDir, "balance.csv")), Read(Path.Combine(csvDir, "band.csv")), File.Exists(income) ? Read(income) : null, method);
    }

    public ModelRow Model(string persona, int horizon)
    {
        var r = Balance(persona, horizon, "model");
        return new ModelRow(Long(r["mae_vnd"]), Long(r["bias_vnd"]));
    }

    public BaselineRow Baseline(string persona, int horizon, string baseline)
    {
        var r = Balance(persona, horizon, baseline);
        return new BaselineRow(Double(r["model_improvement_pct"]), Double(r["ci_low_pct"]), Double(r["ci_high_pct"]));
    }

    public BandRow Band(string persona, int horizon)
    {
        var r = Row(_band, persona, horizon, "K0", "all", null);
        return new BandRow(Double(r["coverage_pct"]), Double(r["ci_low_pct"]), Double(r["ci_high_pct"]), Long(r["mean_width_vnd"]));
    }

    public IncomeRow? Income(string scenario, string window, string persona)
    {
        if (_income is null)
        {
            return null;
        }

        var r = Row(_income, persona, 14, scenario, window, null);
        return new IncomeRow(Double(r["wape_pct"]), Double(r["bias_pct"]));
    }

    private Dictionary<string, string> Balance(string persona, int horizon, string method) => Row(_balance, persona, horizon, "K0", "all", method);

    private Dictionary<string, string> Row(List<Dictionary<string, string>> rows, string persona, int horizon, string scenario, string window, string? method)
    {
        var h = horizon.ToString(CultureInfo.InvariantCulture);
        var match = rows.Where(r => r["mode"] == "oracle" && r["scenario"] == scenario && r["persona"] == persona && r["confidence"] == "all"
                                    && r["horizon"] == h && r["window"] == window && (method is null || r["method"] == method)).ToList();
        return match.Count == 1
            ? match[0]
            : throw new InvalidDataException($"{_dir}: cần đúng một dòng {scenario}/{persona}/{h}/{window}/{method}, có {match.Count}");
    }

    private static List<Dictionary<string, string>> Read(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        var header = lines[0].Split(',');
        return lines.Skip(1).Select(l => header.Zip(l.Split(','), (k, v) => (k, v)).ToDictionary(x => x.k, x => x.v)).ToList();
    }

    private static long Long(string v) => long.Parse(v, CultureInfo.InvariantCulture);

    private static double? Double(string v) => v.Length == 0 ? null : double.Parse(v, CultureInfo.InvariantCulture);
}
