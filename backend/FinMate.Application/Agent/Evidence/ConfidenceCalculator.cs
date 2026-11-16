namespace FinMate.Application.Agent;

/// <summary>Độ tin cậy của một bằng chứng = min(trần theo nguồn, độ đầy đủ × độ mới). Thuần hàm, không đọc đồng hồ: người gọi truyền <c>now</c>.</summary>
public static class ConfidenceCalculator
{
    public static double Compute(EvidenceSource source, DateTimeOffset asOf, DateTimeOffset now, double completeness)
    {
        var clamped = Math.Clamp(completeness, 0.0, 1.0);
        var ageDays = Math.Max(0.0, (now - asOf).TotalDays); // asOf ở tương lai (lệch đồng hồ) coi như mới
        return Math.Min(AgentLimits.ConfidenceCap(source), clamped * Freshness(ageDays));
    }

    private static double Freshness(double ageDays)
    {
        foreach (var (maxAgeDays, factor) in AgentLimits.FreshnessBuckets)
        {
            if (ageDays <= maxAgeDays)
            {
                return factor;
            }
        }

        return AgentLimits.StaleFreshness;
    }
}
