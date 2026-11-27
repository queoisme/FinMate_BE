using System.Text;
using FinMate.Backtest.Evaluation;

namespace FinMate.Backtest.Reporting;

public static partial class ReportWriter
{
    private const string KeyColumns = "mode,scenario,persona,confidence,horizon,window";

    /// <summary>CSV cho tài liệu "AI Dataset and Evaluation": một tệp mỗi nhóm chỉ số, tiền là số nguyên đồng, dấu chấm thập phân.</summary>
    public static IReadOnlyDictionary<string, string> Csv(MetricsResult m) => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["balance.csv"] = Table(
            $"{KeyColumns},method,n,users,mae_vnd,median_ae_vnd,bias_vnd,scale_rel_pct,model_improvement_pct,ci_low_pct,ci_high_pct",
            m.Balance.Select(b => $"{Key(b.Key)},{b.Method},{Format.Csv(b.N)},{Format.Csv(b.Users)},{Format.Csv(b.Mae)},{Format.Csv(b.MedianAe)},{Format.Csv(b.Bias)},"
                                  + $"{Format.Csv(b.ScaleRelPct)},{Format.Csv(b.ImprovementPct)},{Format.Csv(b.CiLow)},{Format.Csv(b.CiHigh)}")),
        ["band.csv"] = Table(
            $"{KeyColumns},n,users,coverage_pct,ci_low_pct,ci_high_pct,mean_width_vnd,median_width_vnd",
            m.Band.Select(b => $"{Key(b.Key)},{Format.Csv(b.N)},{Format.Csv(b.Users)},{Format.Csv(b.CoveragePct)},{Format.Csv(b.CiLow)},{Format.Csv(b.CiHigh)},"
                               + $"{Format.Csv(b.MeanWidth)},{Format.Csv(b.MedianWidth)}")),
        ["flexible.csv"] = Table(
            $"{KeyColumns},method,n,users,wape_pct,ci_low_pct,ci_high_pct,bias_pct,mape_pct,mape_n,mape_excluded",
            m.Flexible.Select(f => $"{Key(f.Key)},{f.Method},{Format.Csv(f.N)},{Format.Csv(f.Users)},{Format.Csv(f.WapePct)},{Format.Csv(f.CiLow)},{Format.Csv(f.CiHigh)},"
                                   + $"{Format.Csv(f.BiasPct)},{Format.Csv(f.MapePct)},{Format.Csv(f.MapeN)},{Format.Csv(f.MapeExcluded)}")),
        ["income.csv"] = Table(
            $"{KeyColumns},n,users,wape_pct,ci_low_pct,ci_high_pct,bias_pct",
            m.Income.Select(i => $"{Key(i.Key)},{Format.Csv(i.N)},{Format.Csv(i.Users)},{Format.Csv(i.WapePct)},{Format.Csv(i.CiLow)},{Format.Csv(i.CiHigh)},{Format.Csv(i.BiasPct)}")),
        ["effects.csv"] = Table(
            "mode,scenario,control,horizon,n,users,mae_scenario_vnd,mae_control_vnd,change_pct,ci_low_pct,ci_high_pct",
            m.Effects.Select(e => $"{e.Mode},{e.Scenario},{e.Control},{Format.Csv(e.Horizon)},{Format.Csv(e.N)},{Format.Csv(e.Users)},{Format.Csv(e.MaeScenario)},"
                                  + $"{Format.Csv(e.MaeControl)},{Format.Csv(e.ChangePct)},{Format.Csv(e.CiLow)},{Format.Csv(e.CiHigh)}")),
    };

    private static string Key(GroupKey k) => $"{k.Mode},{k.Scenario},{k.Persona},{k.Confidence},{Format.Csv(k.Horizon)},{k.Window}";

    private static string Table(string header, IEnumerable<string> lines)
    {
        var builder = new StringBuilder(header).Append('\n');
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }
}
