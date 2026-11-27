using System.Text;
using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Diagnostics;

/// <summary>
/// Phase 97, Task 8: bảng B1/B2 so với dải hiện tại, và áp tiêu chí đã chốt ở §3 của kế hoạch một cách máy móc: chọn phương án có max |phủ − 50%| nhỏ nhất
/// trên các ô hồ sơ × tầm (K0), với điều kiện độ rộng trung bình ở tầm 30 của <c>stable_worker + family</c> không lớn hơn dải hiện tại.
/// </summary>
public static partial class Diagnosis
{
    private const double TargetCoverage = 0.5;
    private const int WidthCheckHorizon = 30;

    private static void WriteSumBands(StringBuilder md, Accumulator acc, IReadOnlyList<string> personas)
    {
        var methods = SumBands.Candidates.Prepend(SumBands.Current).ToList();
        md.Append("### C7. Phase 97 — dải từ tổng h+1 ngày (ô = tỉ lệ số dư thật nằm trong dải / độ rộng trung bình)\n\n");
        md.Append("| Hồ sơ | Tầm | " + string.Join(" | ", methods) + " |\n|---|---|" + string.Concat(methods.Select(_ => "---|")) + "\n");
        foreach (var persona in personas.Append(Evaluation.MetricsBook.AllPersonas))
        {
            foreach (var h in Horizons)
            {
                md.Append($"| {persona} | {h} | {string.Join(" | ", methods.Select(m => CoverageCell(Pooled(acc, Members(persona, personas), h, m))))} |\n");
            }
        }

        var skipped = methods.ToDictionary(m => m, m => personas.Sum(p => Horizons.Sum(h => acc.SumBand[(p, h, m)].Skipped)));
        md.Append($"\nMốc không tính được dải (thiếu lịch sử): {string.Join(", ", methods.Select(m => $"{m} {skipped[m]}"))}.\n\n");

        var currentWidth = Pooled(acc, Regular, WidthCheckHorizon, SumBands.Current).MeanWidth;
        md.Append($"**Áp tiêu chí (§3, K0):** lệch phủ lớn nhất so với 50% (max \\|phủ − 50%\\|) trên {personas.Count * Horizons.Length} ô hồ sơ × tầm, nhỏ hơn thắng; điều kiện: độ rộng TB ở tầm {WidthCheckHorizon} "
                  + $"của stable_worker + family ≤ dải hiện tại ({Money(currentWidth)}).\n\n");
        md.Append("| Phương án | Lệch phủ lớn nhất so với 50% | Ô xa nhất | Độ rộng TB tầm 30, stable_worker + family | Kết quả |\n|---|---|---|---|---|\n");
        var passed = new List<(string Method, double Worst)>();
        foreach (var method in methods)
        {
            var cells = personas.SelectMany(p => Horizons.Select(h => (Cell: $"{p} {h}", Dev: Math.Abs(Pooled(acc, [p], h, method).Rate - TargetCoverage)))).ToList();
            var worst = cells.MaxBy(c => c.Dev);
            var width = Pooled(acc, Regular, WidthCheckHorizon, method).MeanWidth;
            var candidate = method != SumBands.Current;
            var ok = candidate && width <= currentWidth;
            if (ok)
            {
                passed.Add((method, worst.Dev));
            }

            md.Append($"| {method} | {Pct(worst.Dev)} | {worst.Cell} | {Money(width)} | {(candidate ? ok ? "đạt điều kiện" : "không đạt điều kiện độ rộng" : "đối chứng")} |\n");
        }

        var chosen = passed.OrderBy(p => Math.Round(p.Worst, 4)).ThenBy(p => SumBands.Candidates.ToList().IndexOf(p.Method)).Select(p => p.Method).FirstOrDefault();
        md.Append($"\n**Lựa chọn theo tiêu chí:** {chosen ?? "không phương án nào đạt — dừng và báo (D7)"}.\n\n");
    }

    private static (double Rate, double MeanWidth, long N) Pooled(Accumulator acc, IReadOnlyList<string> personas, int h, string method)
    {
        var cells = personas.Select(p => acc.SumBand[(p, h, method)]).ToList();
        var n = cells.Sum(c => c.N);
        return n == 0 ? (0, 0, 0) : ((double)cells.Sum(c => c.Inside) / n, cells.Sum(c => c.Width) / n, n);
    }

    private static string CoverageCell((double Rate, double MeanWidth, long N) c)
        => c.N == 0 ? "—" : $"{Pct(c.Rate)} / {Money(c.MeanWidth)}";
}
