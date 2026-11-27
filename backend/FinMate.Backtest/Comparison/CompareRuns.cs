using System.Globalization;
using System.Text;
using FinMate.Backtest.Reporting;

namespace FinMate.Backtest.Comparison;

/// <summary>
/// Phase 97, Task 11 (D14): <c>--compare-runs ROOT [--notes FILE]</c> đọc CSV của bốn lượt chạy đã có, <c>ROOT/{dev,test}-{before,after}/forecast-backtest/</c>,
/// và viết bảng so sánh. Không chạy lại mô hình và không sửa tay số: mọi con số trong bảng lấy thẳng từ CSV. Phần nhận xét (nếu có) lấy nguyên văn từ FILE và
/// đặt sau các bảng.
/// <para>
/// Ngưỡng "khác đáng kể" giữa tập phát triển và tập kiểm tra chốt ở đây, trước khi chạy tập kiểm tra: số của tập phát triển nằm ngoài khoảng tin cậy 95% của
/// tập kiểm tra (bootstrap theo người dùng, có sẵn trong CSV), với tỉ lệ phủ dải hoặc mức tốt hơn B-hold.
/// </para>
/// </summary>
public static class CompareRuns
{
    public static readonly string[] Sets = ["dev", "test"];
    public static readonly string[] Personas = ["all", "stable_worker", "freelancer", "student", "family", "small_trader"];
    public static readonly int[] Horizons = [7, 14, 30];
    public static readonly string[] Baselines = ["B-hold", "B0", "B-mean30", "B-naive"];

    private const string Csv = "forecast-backtest";

    public static string Run(string root, string? notesPath)
    {
        var runs = Sets.SelectMany(s => new[] { "before", "after" }.Select(w => $"{s}-{w}"))
            .ToDictionary(r => r, r => RunCsv.Load(Path.Combine(root, r, Csv)));
        var md = new StringBuilder();
        md.Append("# Phase 97 — mô hình dự báo số dư trước và sau sửa\n\n");
        md.Append("> Sinh bằng `dotnet run --project FinMate.Backtest -c Release -- --compare-runs <ROOT> --notes <FILE> --out <FILE>` từ CSV của bốn lượt chạy. ");
        md.Append("Bảng không sửa tay. \"Trước\" = mô hình ở `main` @ `e9664f7`; \"sau\" = mô hình cuối của Phase 97. Tập phát triển: hạt 20261127 (dùng để chọn mọi phương án); ");
        md.Append("tập kiểm tra: hạt 20261128 (chạy một lần, sau khi mọi phương án đã chốt). Bộ sinh v1.2, chế độ đáp án, K0 trừ khi ghi khác.\n\n");
        md.Append($"Lượt chạy: {string.Join(", ", runs.Select(r => $"`{r.Key}` ({r.Value.Method})"))}.\n\n");

        AppendLaySummary(md, runs);
        AppendBaselines(md, runs);
        AppendDevVsTest(md, runs);
        AppendIncome(md, runs);
        if (notesPath is not null)
        {
            md.Append(File.ReadAllText(notesPath).TrimEnd()).Append('\n');
        }

        return md.ToString();
    }

    private static void AppendLaySummary(StringBuilder md, IReadOnlyDictionary<string, RunCsv> runs)
    {
        md.Append("## Tóm tắt\n\n");
        md.Append("Cách đọc: **lệch TB** là số tiền dự báo trật trung bình so với số dư thật sau ngần ấy ngày (nhỏ hơn là tốt). **Thiên lệch** cho biết dự báo hay cao hơn (+) hay thấp hơn (−) thực tế. ");
        md.Append("**Trong dải** là tỉ lệ lần số dư thật nằm giữa đường thận trọng và đường lạc quan; mục tiêu khoảng 50%. **Rộng dải** là khoảng cách trung bình giữa hai đường. Tiền: triệu đồng.\n\n");
        foreach (var set in Sets)
        {
            md.Append(set == "dev" ? "### Tập phát triển\n\n" : "### Tập kiểm tra\n\n");
            md.Append("| Hồ sơ | Tầm (ngày) | Lệch TB trước → sau | Giảm | Thiên lệch trước → sau | Trong dải trước → sau | Rộng dải trước → sau |\n|---|---|---|---|---|---|---|\n");
            var (before, after) = (runs[$"{set}-before"], runs[$"{set}-after"]);
            foreach (var p in Personas)
            {
                foreach (var h in Horizons)
                {
                    var (b, a) = (before.Model(p, h), after.Model(p, h));
                    var (bb, ab) = (before.Band(p, h), after.Band(p, h));
                    var drop = b.Mae == 0 ? "—" : P(Math.Round(100.0 * (b.Mae - a.Mae) / b.Mae, 1));
                    md.Append($"| {p} | {h} | {M(b.Mae)} → {M(a.Mae)} | {drop} | {M(b.Bias)} → {M(a.Bias)} | {P(bb.Coverage)} → {P(ab.Coverage)} | {M(bb.Width)} → {M(ab.Width)} |\n");
                }
            }

            md.Append('\n');
        }
    }

    private static void AppendBaselines(StringBuilder md, IReadOnlyDictionary<string, RunCsv> runs)
    {
        md.Append("## So với các cách đoán đơn giản\n\n");
        md.Append("Ô = mô hình tốt hơn cách đoán đó bao nhiêu (1 − lệch TB của mô hình / lệch TB của cách đoán), trước → sau; âm là kém hơn. ");
        md.Append("B-hold: số dư giữ nguyên. B0: chỉ khoản định kỳ. B-mean30: khoản định kỳ + chi trung bình 30 ngày. B-naive: lặp lại dòng tiền gần nhất.\n\n");
        foreach (var set in Sets)
        {
            md.Append(set == "dev" ? "### Tập phát triển\n\n" : "### Tập kiểm tra\n\n");
            md.Append("| Hồ sơ | Tầm | " + string.Join(" | ", Baselines) + " |\n|---|---|" + string.Concat(Baselines.Select(_ => "---|")) + "\n");
            foreach (var p in Personas)
            {
                foreach (var h in Horizons)
                {
                    var cells = Baselines.Select(bl => $"{P(runs[$"{set}-before"].Baseline(p, h, bl).Improvement)} → {P(runs[$"{set}-after"].Baseline(p, h, bl).Improvement)}");
                    md.Append($"| {p} | {h} | {string.Join(" | ", cells)} |\n");
                }
            }

            md.Append('\n');
        }
    }

    private static void AppendDevVsTest(StringBuilder md, IReadOnlyDictionary<string, RunCsv> runs)
    {
        md.Append("## Tập phát triển so với tập kiểm tra (mô hình sau)\n\n");
        md.Append("**Khác đáng kể** (ngưỡng chốt trước khi chạy tập kiểm tra): số của tập phát triển nằm ngoài khoảng tin cậy 95% của tập kiểm tra, với tỉ lệ trong dải hoặc với mức tốt hơn B-hold.\n\n");
        md.Append("| Hồ sơ | Tầm | Lệch TB dev / test | Thiên lệch dev / test | Trong dải dev / test (CI test) | Tốt hơn B-hold dev / test (CI test) | Khác đáng kể |\n|---|---|---|---|---|---|---|\n");
        var (dev, test) = (runs["dev-after"], runs["test-after"]);
        var flagged = 0;
        foreach (var p in Personas)
        {
            foreach (var h in Horizons)
            {
                var (dm, tm) = (dev.Model(p, h), test.Model(p, h));
                var (db, tb) = (dev.Band(p, h), test.Band(p, h));
                var (dh, th) = (dev.Baseline(p, h, "B-hold"), test.Baseline(p, h, "B-hold"));
                var marks = new List<string>();
                if (Outside(db.Coverage, tb.CiLow, tb.CiHigh))
                {
                    marks.Add("trong dải");
                }

                if (Outside(dh.Improvement, th.CiLow, th.CiHigh))
                {
                    marks.Add("so với B-hold");
                }

                flagged += marks.Count > 0 ? 1 : 0;
                md.Append($"| {p} | {h} | {M(dm.Mae)} / {M(tm.Mae)} | {M(dm.Bias)} / {M(tm.Bias)} | {P(db.Coverage)} / {P(tb.Coverage)} ({P(tb.CiLow)} – {P(tb.CiHigh)}) | "
                          + $"{P(dh.Improvement)} / {P(th.Improvement)} ({P(th.CiLow)} – {P(th.CiHigh)}) | {(marks.Count == 0 ? "—" : "**có**: " + string.Join(", ", marks))} |\n");
            }
        }

        md.Append($"\nSố ô khác đáng kể: {flagged} / {Personas.Length * Horizons.Length}.\n\n");
    }

    private static void AppendIncome(StringBuilder md, IReadOnlyDictionary<string, RunCsv> runs)
    {
        md.Append("## Thu không định kỳ (tầm 14, mô hình sau)\n\n");
        md.Append("Mô hình trước không chiếu thu không định kỳ (thiên lệch −100%). Ô = WAPE / thiên lệch so với thực tế. K2: khoản thu định kỳ về trễ một tháng, rồi đổi số tiền vĩnh viễn.\n\n");
        md.Append("| Kịch bản | Mốc | Hồ sơ | Tập phát triển | Tập kiểm tra |\n|---|---|---|---|---|\n");
        foreach (var (scenario, window) in new[] { ("K0", "all"), ("K2", "all"), ("K2", "affected") })
        {
            foreach (var p in new[] { "all", "freelancer", "student", "small_trader" })
            {
                md.Append($"| {scenario} | {(window == "all" ? "mọi mốc" : "bị ảnh hưởng")} | {p} | {IncomeCell(runs["dev-after"], scenario, window, p)} | {IncomeCell(runs["test-after"], scenario, window, p)} |\n");
            }
        }

        md.Append('\n');
    }

    private static string IncomeCell(RunCsv run, string scenario, string window, string persona)
        => run.Income(scenario, window, persona) is { } i ? $"{P(i.Wape)} / {P(i.Bias)}" : "—";

    private static bool Outside(double? value, double? low, double? high)
        => value is { } v && low is { } l && high is { } h && (v < l || v > h);

    private static string P(double? value) => Format.Pct(value).Replace("-", "−");

    private static string M(long cents) => (cents / 1e6).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',').Replace("-", "−");
}
