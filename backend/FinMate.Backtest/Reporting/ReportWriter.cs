using System.Text;
using FinMate.Backtest.Evaluation;
using FinMate.Backtest.Generation;

namespace FinMate.Backtest.Reporting;

/// <summary>Thông tin tái lập in đầu báo cáo (D13, S4). Không có thời gian chạy hay ngày giờ: hai lượt cùng đầu vào phải ra cùng từng byte.</summary>
public sealed record RunInfo(
    string Commit,
    string Runtime,
    string GeneratorVersion,
    ulong RootSeed,
    int SeedsPerPersona,
    int StepDays,
    int ConfigVersion,
    string ConfigSha256,
    int Worlds,
    int Rows);

/// <summary>Sinh <c>forecast-backtest.md</c> và các CSV từ kết quả chỉ số. Toàn bộ do chương trình viết; không sửa tay (D13).</summary>
public static partial class ReportWriter
{
    private const string Control = ScenarioCodes.K0;
    private const string All = MetricsBook.AllPersonas;

    private static readonly Dictionary<string, string> ScenarioNames = new()
    {
        [ScenarioCodes.K0] = "K0 đối chứng",
        [ScenarioCodes.K1] = "K1 chi đột xuất lớn",
        [ScenarioCodes.K2] = "K2 thu nhập về trễ / đổi số tiền",
        [ScenarioCodes.K3] = "K3 bỏ một kỳ định kỳ",
        [ScenarioCodes.K4] = "K4 khoản định kỳ đổi giá",
        [ScenarioCodes.K5] = "K5 Tết, chưa khai ngày lễ",
        [ScenarioCodes.K5h] = "K5h Tết, đã khai ngày lễ 150%",
        [ScenarioCodes.K6] = "K6 người dùng mới",
    };

    private static readonly Dictionary<string, string> MethodNames = new()
    {
        [Methods.Model] = "Mô hình",
        [Methods.Hold] = "B-hold (số dư giữ nguyên)",
        [Methods.RecurringOnly] = "B0 (chỉ khoản định kỳ)",
        [Methods.Mean30] = "B-mean30 (trung bình 30 ngày)",
        [Methods.Naive] = "B-naive (lặp lại h+1 ngày trước)",
    };

    public static string Markdown(RunInfo info, MetricsResult metrics)
    {
        var md = new StringBuilder();
        md.Append("# Backtest dự báo số dư — Phase 96, tầng 1\n\n");
        md.Append("> Tệp do `FinMate.Backtest` sinh ra, không sửa tay. Dữ liệu là **dữ liệu tổng hợp**, không có dữ liệu người dùng thật.\n");
        md.Append("> Chế độ duy nhất của tầng 1 là **đáp án**: mô hình được biết đúng các khoản định kỳ (chỉ với thông tin trước mốc), nên đây là **cận trên** của độ chính xác.\n\n");
        Header(md, info);
        Summary(md, metrics);
        BalanceByHorizon(md, metrics);
        ScenarioTable(md, metrics);
        PersonaTable(md, metrics);
        ConfidenceTable(md, metrics);
        BandTable(md, metrics);
        FlexTable(md, metrics);
        Assumptions(md, info);
        return md.ToString();
    }

    private static void Header(StringBuilder md, RunInfo info)
    {
        md.Append("## Tái lập\n\n");
        md.Append("| Mục | Giá trị |\n|---|---|\n");
        md.Append($"| Commit | `{info.Commit}` |\n");
        md.Append($"| SDK / runtime | {info.Runtime} |\n");
        md.Append($"| Đặc tả bộ sinh | v{info.GeneratorVersion} (`docs/evaluation/forecast-backtest-generator-spec.md`) |\n");
        md.Append($"| Hạt gốc | {info.RootSeed} |\n");
        md.Append($"| Người gốc | {GeneratorSpec.Personas.Count} hồ sơ × {info.SeedsPerPersona} hạt giống |\n");
        md.Append($"| Bước mốc | {info.StepDays} ngày, từ {GeneratorSpec.EvaluationStart:yyyy-MM-dd} tới {GeneratorSpec.EvaluationEnd:yyyy-MM-dd} |\n");
        md.Append($"| Tham số `forecast` | phiên bản {info.ConfigVersion}, SHA-256 `{info.ConfigSha256}` |\n");
        md.Append($"| Thế giới / dòng kết quả | {info.Worlds} / {info.Rows} |\n");
        md.Append("\nCùng image SDK, cùng hạt, cùng tham số → cùng tệp này từng byte (S4).\n\n");
    }

    private static BalanceMetric? Balance(MetricsResult m, string scenario, int horizon, string method, string persona = All, string confidence = All, string window = ScoreWindows.All)
        => m.Balance.FirstOrDefault(b => b.Key.Mode == Modes.Oracle && b.Key.Scenario == scenario && b.Key.Horizon == horizon && b.Method == method
            && b.Key.Persona == persona && b.Key.Confidence == confidence && b.Key.Window == window);

    private static BandMetric? Band(MetricsResult m, string scenario, int horizon, string persona = All, string confidence = All)
        => m.Band.FirstOrDefault(b => b.Key.Mode == Modes.Oracle && b.Key.Scenario == scenario && b.Key.Horizon == horizon && b.Key.Persona == persona
            && b.Key.Confidence == confidence && b.Key.Window == ScoreWindows.All);

    private static FlexMetric? Flex(MetricsResult m, string scenario, int horizon, string method)
        => m.Flexible.FirstOrDefault(f => f.Key.Mode == Modes.Oracle && f.Key.Scenario == scenario && f.Key.Horizon == horizon && f.Method == method
            && f.Key.Persona == All && f.Key.Confidence == All && f.Key.Window == ScoreWindows.All);

    private static string Name(string scenario) => ScenarioNames.GetValueOrDefault(scenario, scenario);
}
