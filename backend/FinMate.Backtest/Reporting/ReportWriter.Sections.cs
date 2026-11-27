using System.Text;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Reporting;

public static partial class ReportWriter
{
    private const int SummaryHorizon = 14;
    private const string ModeLabel = "chế độ đáp án";

    /// <summary>Bảng tóm tắt cho người không chuyên: mỗi dòng một câu, ghi rõ chế độ (S5/D13).</summary>
    private static void Summary(StringBuilder md, MetricsResult m)
    {
        md.Append("## Tóm tắt\n\n| # | Kết quả |\n|---|---|\n");
        var lines = new List<string>();
        foreach (var h in BacktestRunner.Horizons)
        {
            lines.Add(BalanceSentence(m, h));
        }

        foreach (var persona in GeneratorSpec.Personas)
        {
            lines.Add(PersonaSentence(m, persona));
        }

        lines.Add(FlexSentence(m));
        lines.Add(BandSentence(m));
        foreach (var effect in m.Effects.Where(e => e.Horizon == SummaryHorizon && e.Mode == Modes.Oracle))
        {
            lines.Add(EffectSentence(effect));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            md.Append($"| {i + 1} | {lines[i]} |\n");
        }

        md.Append("\nCI 95% lấy bằng bootstrap theo người gốc (1.000 lần). \"Tốt hơn X%\" = 1 − MAE mô hình / MAE baseline; âm là kém hơn.\n\n");
    }

    private static string BalanceSentence(MetricsResult m, int h)
    {
        var model = Balance(m, Control, h, Methods.Model);
        if (model is null)
        {
            return $"Tầm {h} ngày: không có dữ liệu.";
        }

        var hold = Balance(m, Control, h, Methods.Hold)!;
        var strongest = Methods.Balance.Skip(1).Select(b => Balance(m, Control, h, b)!).Where(b => b.Method != Methods.Hold).MinBy(b => b.Mae)!;
        return $"Tầm {h} ngày, {ModeLabel}, {Name(Control)}: số dư lệch trung bình {Format.Money(model.Mae)} "
               + $"({Format.Pct(model.ScaleRelPct)} quy mô dòng tiền thực tế), trung vị {Format.Money(model.MedianAe)}. "
               + $"Tốt hơn \"số dư giữ nguyên\" {Format.PctCi(hold.ImprovementPct, hold.CiLow, hold.CiHigh, hold.N)}; "
               + $"so với baseline mạnh nhất còn lại ({MethodNames[strongest.Method]}): {Format.PctCi(strongest.ImprovementPct, strongest.CiLow, strongest.CiHigh, strongest.N)}.";
    }

    private static string PersonaSentence(MetricsResult m, string persona)
    {
        var model = Balance(m, Control, SummaryHorizon, Methods.Model, persona);
        if (model is null)
        {
            return $"Hồ sơ {persona}: không có dữ liệu.";
        }

        var hold = Balance(m, Control, SummaryHorizon, Methods.Hold, persona)!;
        var direction = model.Bias < 0 ? "thấp hơn" : "cao hơn";
        return $"Hồ sơ {persona}, tầm {SummaryHorizon} ngày, {ModeLabel}, {Name(Control)}: số dư lệch trung bình {Format.Money(model.Mae)}, "
               + $"dự báo trung bình {direction} thực tế {Format.Money(Math.Abs(model.Bias))}; tốt hơn \"số dư giữ nguyên\" {Format.PctCi(hold.ImprovementPct, hold.CiLow, hold.CiHigh, hold.N)}.";
    }

    private static string FlexSentence(MetricsResult m)
    {
        var model = Flex(m, Control, SummaryHorizon, Methods.Model);
        var mean = Flex(m, Control, SummaryHorizon, Methods.Mean30);
        var naive = Flex(m, Control, SummaryHorizon, Methods.Naive);
        return model is null
            ? "Chi linh hoạt: không có dữ liệu."
            : $"Chi linh hoạt {SummaryHorizon + 1} ngày tới (tầm {SummaryHorizon}), {ModeLabel}: lệch WAPE {Format.PctCi(model.WapePct, model.CiLow, model.CiHigh, model.N)}, "
              + $"thiên lệch {Format.Pct(model.BiasPct)} (âm = dự báo chi ít hơn thực tế). B-mean30: {Format.Pct(mean?.WapePct)}, B-naive: {Format.Pct(naive?.WapePct)}.";
    }

    private static string BandSentence(MetricsResult m)
    {
        var parts = BacktestRunner.Horizons.Select(h => Band(m, Control, h)).Where(b => b is not null)
            .Select(b => $"tầm {b!.Key.Horizon}: {Format.PctCi(b.CoveragePct, b.CiLow, b.CiHigh, b.N)}, rộng trung bình {Format.Money(b.MeanWidth)}");
        return $"Dải thận trọng–lạc quan (P25–P75 của tổng dòng tiền linh hoạt ròng, NetFlowBand), {ModeLabel}, {Name(Control)}: tỉ lệ số dư thật nằm trong dải — "
               + string.Join("; ", parts) + ". Một dải P25–P75 đúng nghĩa của tổng sẽ chứa khoảng 50%.";
    }

    private static string EffectSentence(ScenarioEffect e)
    {
        var verb = e.ChangePct is >= 0 ? "tăng" : "giảm";
        var change = e.N < Format.MinSamples ? Format.NotEnough : $"{verb} {Format.Pct(Math.Abs(e.ChangePct ?? 0))} (CI 95%: {Format.Pct(e.CiLow)} – {Format.Pct(e.CiHigh)})";
        return $"{Name(e.Scenario)} (tầm {e.Horizon} ngày, {ModeLabel}, chỉ các mốc bị kịch bản ảnh hưởng, n = {e.N}): sai số số dư {change} "
               + $"so với {Name(e.Control)} trên cùng người và mốc ({Format.Money(e.MaeScenario)} so với {Format.Money(e.MaeControl)}).";
    }

    private static void BalanceByHorizon(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 1. Số dư theo tầm và phương pháp ({Name(Control)}, {ModeLabel}, mọi hồ sơ)\n\n");
        md.Append("| Tầm | Phương pháp | n | MAE | Trung vị | Thiên lệch | % quy mô | Mô hình tốt hơn |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var h in BacktestRunner.Horizons)
        {
            foreach (var method in Methods.Balance)
            {
                if (Balance(m, Control, h, method) is not { } b)
                {
                    continue;
                }

                var better = method == Methods.Model ? "—" : Format.PctCi(b.ImprovementPct, b.CiLow, b.CiHigh, b.N);
                md.Append($"| {h} | {MethodNames[method]} | {b.N} | {Format.Money(b.Mae)} | {Format.Money(b.MedianAe)} | {Format.Money(b.Bias)} | {Format.Pct(b.ScaleRelPct)} | {better} |\n");
            }
        }

        md.Append("\nThiên lệch = trung bình (dự kiến − thực tế); âm là dự báo thấp hơn thực tế. % quy mô = MAE / trung bình |số dư thực tế − số dư lúc dự báo|.\n\n");
    }

    private static void ScenarioTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 2. Theo kịch bản ({ModeLabel}, mọi hồ sơ, mô hình)\n\n");
        md.Append("| Kịch bản | Tầm | MAE cả năm (n) | MAE mốc bị ảnh hưởng (n) | So với đối chứng cùng mốc |\n|---|---|---|---|---|\n");
        foreach (var scenario in m.Balance.Select(b => b.Key.Scenario).Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            foreach (var h in BacktestRunner.Horizons)
            {
                var all = Balance(m, scenario, h, Methods.Model);
                var affected = Balance(m, scenario, h, Methods.Model, window: ScoreWindows.Affected);
                var effects = m.Effects.Where(e => e.Scenario == scenario && e.Horizon == h && e.Mode == Modes.Oracle)
                    .Select(e => $"{e.Control}: {(e.N < Format.MinSamples ? Format.NotEnough : $"{Format.Pct(e.ChangePct)} (CI {Format.Pct(e.CiLow)} – {Format.Pct(e.CiHigh)})")}");
                md.Append($"| {Name(scenario)} | {h} | {Cell(all)} | {Cell(affected)} | {string.Join("; ", effects).OrEmptyDash()} |\n");
            }
        }

        md.Append("\nMốc bị ảnh hưởng: đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (với K6: mọi mốc 14–75 ngày sau ngày bắt đầu). So với đối chứng: MAE kịch bản / MAE đối chứng − 1 trên cùng (người, mốc, tầm).\n\n");
    }

    private static void PersonaTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 3. Theo hồ sơ ({Name(Control)}, tầm {SummaryHorizon}, {ModeLabel})\n\n");
        md.Append("| Hồ sơ | n | MAE mô hình | Thiên lệch | % quy mô | Tốt hơn B-hold | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var persona in GeneratorSpec.Personas)
        {
            var model = Balance(m, Control, SummaryHorizon, Methods.Model, persona);
            if (model is null)
            {
                continue;
            }

            var band = Band(m, Control, SummaryHorizon, persona);
            md.Append($"| {persona} | {model.N} | {Format.Money(model.Mae)} | {Format.Money(model.Bias)} | {Format.Pct(model.ScaleRelPct)} | {Better(m, persona, Methods.Hold)} | "
                      + $"{Better(m, persona, Methods.Mean30)} | {Better(m, persona, Methods.Naive)} | {(band is null ? "—" : Format.PctCi(band.CoveragePct, band.CiLow, band.CiHigh, band.N))} |\n");
        }

        md.Append('\n');
    }

    private static void ConfidenceTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 4. Theo mức tin cậy mô hình tự báo ({ModeLabel}, tầm {SummaryHorizon}, mọi kịch bản gộp theo từng kịch bản)\n\n");
        md.Append("| Kịch bản | Tin cậy | n | MAE | % quy mô |\n|---|---|---|---|---|\n");
        foreach (var scenario in new[] { Control, ScenarioCodes.K6 })
        {
            foreach (var confidence in new[] { "normal", "low" })
            {
                var b = Balance(m, scenario, SummaryHorizon, Methods.Model, confidence: confidence);
                md.Append($"| {Name(scenario)} | {confidence} | {b?.N ?? 0} | {(b is null ? "—" : Format.Money(b.Mae))} | {Format.Pct(b?.ScaleRelPct)} |\n");
            }
        }

        md.Append('\n');
    }

    private static void BandTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 5. Dải thận trọng–lạc quan theo tầm ({ModeLabel}, mọi hồ sơ)\n\n");
        md.Append("Dải của engine (Phase 97, `NetFlowBand`) = dự kiến ± 0,6745·σ_L, σ_L² = σ²L + σ²L²/n, với σ² là phương sai phần dư của dòng tiền linh hoạt ròng mỗi ngày ");
        md.Append("(thu linh hoạt − chi linh hoạt) quanh mức trung bình theo thứ, L = số ngày tính cả hôm nay, n = số ngày lịch sử; khoản cho vay đến hạn chỉ cộng vào đường lạc quan. ");
        md.Append("Dưới `minHistoryDays` engine giữ dải cũ (cộng P75/P25 chi theo từng ngày). Dải P25–P75 đúng nghĩa sẽ chứa khoảng 50%.\n\n");
        md.Append("| Kịch bản | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình | Rộng trung vị |\n|---|---|---|---|---|---|\n");
        foreach (var scenario in m.Band.Select(b => b.Key.Scenario).Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            foreach (var h in BacktestRunner.Horizons)
            {
                if (Band(m, scenario, h) is { } b)
                {
                    md.Append($"| {Name(scenario)} | {h} | {b.N} | {Format.PctCi(b.CoveragePct, b.CiLow, b.CiHigh, b.N)} | {Format.Money(b.MeanWidth)} | {Format.Money(b.MedianWidth)} |\n");
                }
            }
        }

        md.Append($"\nTheo hồ sơ ({Name(Control)}):\n\n| Hồ sơ | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình |\n|---|---|---|---|---|\n");
        foreach (var persona in GeneratorSpec.Personas)
        {
            foreach (var h in BacktestRunner.Horizons)
            {
                if (Band(m, Control, h, persona) is { } b)
                {
                    md.Append($"| {persona} | {h} | {b.N} | {Format.PctCi(b.CoveragePct, b.CiLow, b.CiHigh, b.N)} | {Format.Money(b.MeanWidth)} |\n");
                }
            }
        }

        md.Append('\n');
    }

    private static void FlexTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 6. Chi linh hoạt ({Name(Control)}, {ModeLabel}, mọi hồ sơ)\n\n");
        md.Append("| Tầm | Phương pháp | n | WAPE | Thiên lệch | MAPE (n, bị loại) |\n|---|---|---|---|---|---|\n");
        foreach (var h in BacktestRunner.Horizons)
        {
            foreach (var method in Methods.Flexible)
            {
                if (Flex(m, Control, h, method) is { } f)
                {
                    md.Append($"| {h} | {MethodNames[method]} | {f.N} | {Format.PctCi(f.WapePct, f.CiLow, f.CiHigh, f.N)} | {Format.Pct(f.BiasPct)} | {Format.Pct(f.MapePct)} ({f.MapeN}, {f.MapeExcluded}) |\n");
                }
            }
        }

        md.Append($"\nMAPE chỉ tính trên mốc có chi linh hoạt thực tế ≥ {Format.Money(MetricsBook.MapeFloorCents)}.\n\n");
    }

    /// <summary>Phase 97 (S3): thu không định kỳ mô hình chiếu so với thực tế, theo kịch bản; K2 in riêng để thấy ảnh hưởng của thu về trễ / đổi số tiền.</summary>
    private static void IncomeTable(StringBuilder md, MetricsResult m)
    {
        md.Append($"## 6b. Thu không định kỳ ({ModeLabel}, mọi hồ sơ có thu không định kỳ)\n\n");
        md.Append("| Kịch bản | Tầm | Cửa sổ | n | WAPE | Thiên lệch |\n|---|---|---|---|---|---|\n");
        foreach (var i in m.Income.Where(i => i.Key.Mode == Modes.Oracle && i.Key.Persona == All && i.Key.Confidence == MetricsBook.AllConfidence)
                     .OrderBy(i => i.Key.Scenario, StringComparer.Ordinal).ThenBy(i => i.Key.Horizon).ThenBy(i => i.Key.Window, StringComparer.Ordinal))
        {
            md.Append($"| {Name(i.Key.Scenario)} | {i.Key.Horizon} | {i.Key.Window} | {i.N} | {Format.PctCi(i.WapePct, i.CiLow, i.CiHigh, i.N)} | {Format.Pct(i.BiasPct)} |\n");
        }

        md.Append("\nThu không định kỳ = khoản thu không phải khoản thu định kỳ của đáp án, không vay, không hoàn. Thiên lệch âm: mô hình chiếu ít hơn thực tế. "
                  + "Trước Phase 97 mô hình không chiếu khoản nào (thiên lệch −100%).\n\n");
    }

    private static void Assumptions(StringBuilder md, RunInfo info)
    {
        md.Append("## 7. Bộ sinh dữ liệu giả định gì\n\n");
        md.Append($"Chi tiết đầy đủ ở đặc tả v{info.GeneratorVersion}. Tóm tắt:\n\n");
        md.Append("- Năm hồ sơ: lương cố định, freelancer thu không đều, sinh viên (trợ cấp + làm thêm + học phí theo kỳ), gia đình (ba ví, một ví không tính vào dự báo, vay, quỹ nhóm, gửi tiết kiệm), tiểu thương (thu hằng ngày, nhập hàng hằng tuần dao động).\n");
        md.Append("- Chi linh hoạt: số lần mỗi ngày theo Poisson, số tiền log-normal (đuôi nặng), hệ số thứ trong tuần, chi tăng 10–30% trong 5 ngày sau ngày nhận tiền lớn, mức chi trôi −1% đến +2% mỗi tháng. Mô hình không biết các hiệu ứng này.\n");
        md.Append("- Khoản định kỳ trả lệch −2 đến +3 ngày; hóa đơn điện tăng theo mùa hè; nước dao động ±15%; 3% khoản mua sắm được hoàn tiền (đủ hoặc một phần).\n");
        md.Append("- Tên nơi chi đôi khi viết hoa / thêm khoảng trắng / bỏ dấu, nhưng không có biến thể kiểu ngân hàng (\"EVN HCMC 0123\").\n");
        md.Append("- Kịch bản cài vào: chi đột xuất lớn, thu nhập trễ rồi đổi số tiền, bỏ một kỳ hóa đơn, đổi giá (+5% và +25%), Tết 2026 (17/2/2026, nguồn trong đặc tả) có và không khai ngày lễ, người dùng mới.\n");
        md.Append("- Vay, quỹ nhóm, chuyển ví, hoàn tiền dùng đúng loại giao dịch production lưu (Q16); repository giả được kiểm bằng test hợp đồng với Postgres thật.\n\n");
        md.Append("## 8. Hạn chế\n\n");
        md.Append("- **Dữ liệu tổng hợp**: con số cho biết mô hình làm gì trên các mẫu hành vi đã nêu, không phải độ chính xác trên người dùng thật.\n");
        md.Append($"- **Cỡ mẫu**: {GeneratorSpec.Personas.Count * info.SeedsPerPersona} người gốc; các mốc của cùng một người tương quan với nhau, nên CI lấy theo người, không theo mốc.\n");
        md.Append("- **Chế độ đáp án là cận trên**: mô hình được biết trước khoản nào là định kỳ. Chế độ phát lại (tự phát hiện định kỳ) thuộc tầng 2, chưa chạy.\n");
        md.Append("- Không có phản hồi người dùng (đánh dấu loại khỏi dự báo, xác nhận bất thường), không có cặp đảo hay liên kết trùng, không có giao dịch nháp chưa xác nhận.\n");
        md.Append("- Mục tiêu tiết kiệm không được đo: khoản góp do bộ sinh tự đặt, đo nó là tự chấm bài.\n\n");
        md.Append("## 9. Chạy lại\n\n");
        md.Append("```\ncd backend\ndotnet run --project FinMate.Backtest -c Release -- --out ../docs/evaluation\n```\n\n");
        md.Append("Chạy trong container `mcr.microsoft.com/dotnet/sdk:9.0` với thư mục gốc repo được mount (như mọi lệnh backend). Không cần Postgres, Redis hay Docker compose.\n");
    }

    private static string Cell(BalanceMetric? b) => b is null ? "—" : $"{Format.Money(b.Mae)} ({b.N})";

    private static string Better(MetricsResult m, string persona, string method)
        => Balance(m, Control, SummaryHorizon, method, persona) is { } b ? Format.PctCi(b.ImprovementPct, b.CiLow, b.CiHigh, b.N) : "—";

    private static string OrEmptyDash(this string value) => value.Length == 0 ? "—" : value;
}
