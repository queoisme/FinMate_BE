using System.Text;
using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Diagnostics;

public static class EstimatorKinds
{
    public const string Flex = "flex";
    public const string Income = "income";
    public const string Production = "production";
}

/// <summary>
/// Phase 97, Task 1: bảng F1–F3 cho chi và thu linh hoạt, và áp tiêu chí chọn đã chốt trước (D6 sửa bởi S2) một cách máy móc, để lựa chọn không
/// phụ thuộc người đọc bảng. Tiêu chí xét ở tầm 14, gộp mọi hồ sơ.
/// </summary>
public static partial class Diagnosis
{
    private const int SelectionHorizon = 14;
    private const double MaxBias = 0.05;
    private const double MaxPersonaWorse = 0.05;
    private const double StudentMaxWorse = 0.15;
    private const double StudentMaxBias = 0.10;

    private static void WriteEstimators(StringBuilder md, Accumulator acc, IReadOnlyList<string> personas)
    {
        md.Append("### C6. Phase 97 — F1–F3 cho chi và thu linh hoạt (ô = WAPE / thiên lệch so với thực tế)\n\n");
        foreach (var kind in new[] { EstimatorKinds.Flex, EstimatorKinds.Income })
        {
            md.Append(kind == EstimatorKinds.Flex ? "Chi linh hoạt:\n\n" : "Thu linh hoạt (thu không định kỳ; production = 0 vì không chiếu):\n\n");
            var methods = new[] { EstimatorKinds.Production }.Concat(Estimators.All).ToList();
            md.Append("| Hồ sơ | Tầm | " + string.Join(" | ", methods) + " |\n|---|---|" + string.Concat(methods.Select(_ => "---|")) + "\n");
            foreach (var persona in personas.Append(Evaluation.MetricsBook.AllPersonas))
            {
                foreach (var h in Horizons)
                {
                    md.Append($"| {persona} | {h} | {string.Join(" | ", methods.Select(m => EstimateCell(acc, Members(persona, personas), h, kind, m)))} |\n");
                }
            }

            md.Append('\n');
        }

        WriteSelection(md, acc, personas);
    }

    private static void WriteSelection(StringBuilder md, Accumulator acc, IReadOnlyList<string> personas)
    {
        md.Append($"**Áp tiêu chí (tầm {SelectionHorizon}, gộp):** |thiên lệch chi| ≤ 5% và |thiên lệch thu| ≤ 5% (xét riêng); WAPE chi của mỗi hồ sơ không tệ hơn production quá 5 điểm % "
                  + "(sinh viên: tới 15 điểm nếu |thiên lệch chi của sinh viên| ≤ 10%); qua hết thì xếp theo WAPE gộp chi + thu; hòa thì F1 < F3 < F2.\n\n");
        md.Append("| Phương án | Thiên lệch chi | Thiên lệch thu | Hồ sơ vi phạm | WAPE gộp chi + thu | Kết quả |\n|---|---|---|---|---|---|\n");
        var passed = new List<(string Method, double Wape)>();
        foreach (var method in Estimators.All)
        {
            var flex = Ratio(acc, personas, SelectionHorizon, EstimatorKinds.Flex, method);
            var income = Ratio(acc, personas, SelectionHorizon, EstimatorKinds.Income, method);
            var violations = personas.Where(p => PersonaViolates(acc, p, method)).ToList();
            var combined = CombinedWape(acc, personas, method);
            var ok = Math.Abs(flex.Bias) <= MaxBias && Math.Abs(income.Bias) <= MaxBias && violations.Count == 0;
            if (ok)
            {
                passed.Add((method, combined));
            }

            md.Append($"| {method} | {Pct(flex.Bias)} | {Pct(income.Bias)} | {(violations.Count == 0 ? "—" : string.Join(", ", violations))} | {Pct(combined)} | {(ok ? "đạt" : "không đạt")} |\n");
        }

        var chosen = passed.OrderBy(p => Math.Round(p.Wape, 4)).ThenBy(p => Simplicity(p.Method)).Select(p => p.Method).FirstOrDefault();
        md.Append($"\n**Lựa chọn theo tiêu chí:** {chosen ?? "không phương án nào đạt — dừng và báo (D6)"}.\n\n");
    }

    private static bool PersonaViolates(Accumulator acc, string persona, string method)
    {
        var candidate = Ratio(acc, [persona], SelectionHorizon, EstimatorKinds.Flex, method);
        var production = Ratio(acc, [persona], SelectionHorizon, EstimatorKinds.Flex, EstimatorKinds.Production);
        var worse = candidate.Wape - production.Wape;
        return persona == Persona.Student
            ? worse > StudentMaxWorse || (worse > MaxPersonaWorse && Math.Abs(candidate.Bias) > StudentMaxBias)
            : worse > MaxPersonaWorse;
    }

    private static double CombinedWape(Accumulator acc, IReadOnlyList<string> personas, string method)
    {
        var sums = personas.SelectMany(p => new[] { EstimatorKinds.Flex, EstimatorKinds.Income }.Select(k => acc.Estimates[(p, SelectionHorizon, k, method)])).ToList();
        return sums.Sum(s => s.Absolute) / sums.Sum(s => s.Actual);
    }

    private static (double Wape, double Bias) Ratio(Accumulator acc, IReadOnlyList<string> personas, int h, string kind, string method)
    {
        var sums = personas.Select(p => acc.Estimates[(p, h, kind, method)]).ToList();
        var actual = sums.Sum(s => s.Actual);
        return actual <= 0 ? (0, 0) : (sums.Sum(s => s.Absolute) / actual, sums.Sum(s => s.Signed) / actual);
    }

    private static string EstimateCell(Accumulator acc, IReadOnlyList<string> personas, int h, string kind, string method)
    {
        var sums = personas.Select(p => acc.Estimates[(p, h, kind, method)]).ToList();
        var actual = sums.Sum(s => s.Actual);
        return actual <= 0 ? "— (thực tế 0)" : $"{Pct(sums.Sum(s => s.Absolute) / actual)} / {Pct(sums.Sum(s => s.Signed) / actual)}";
    }

    private static IReadOnlyList<string> Members(string persona, IReadOnlyList<string> personas)
        => persona == Evaluation.MetricsBook.AllPersonas ? personas : [persona];

    private static int Simplicity(string method) => method == Estimators.F1 ? 0 : method == Estimators.F3 ? 1 : 2;
}
