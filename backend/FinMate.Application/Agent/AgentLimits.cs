namespace FinMate.Application.Agent;

/// <summary>
/// MỌI ngưỡng của agent nằm ở đây, mỗi ngưỡng một hằng số (NFR-MNT-08). Giá trị đánh dấu "provisional" là đề xuất ban đầu của kế hoạch
/// docs/superpowers/plans/2026-11-07-ai-financial-agent.md, chưa được đo: chỉnh sau khi có số từ bộ đánh giá agent_eval, không rải số ra nơi khác.
/// </summary>
public static class AgentLimits
{
    /// <summary>Số bước tối đa trong một kế hoạch (provisional).</summary>
    public const int MaxPlanSteps = 4;

    /// <summary>Số lần lập lại kế hoạch tối đa khi thiếu dữ liệu (provisional).</summary>
    public const int MaxReplans = 2;

    /// <summary>Dưới số tháng lịch sử này thì không dựng dải thu nhập, trả InsufficientData (provisional).</summary>
    public const int MinHistoryMonths = 3;

    /// <summary>Hệ số biến thiên thu nhập từ mức này trở lên coi là nhóm thu nhập không ổn định (provisional).</summary>
    public const double VariableSegmentCv = 0.25;

    /// <summary>Độ mới của bằng chứng: tuổi (ngày) tối đa → hệ số. Quá bậc cuối thì dùng <see cref="StaleFreshness"/> (provisional).</summary>
    public static readonly (int MaxAgeDays, double Factor)[] FreshnessBuckets = [(2, 1.0), (7, 0.8), (30, 0.5)];

    public const double StaleFreshness = 0.2;

    /// <summary>Trần độ tin cậy theo nguồn (provisional): dữ liệu ước tính hay do người dùng tự nhập không bao giờ chắc bằng dữ liệu đã xác nhận.</summary>
    public static double ConfidenceCap(EvidenceSource source) => source switch
    {
        EvidenceSource.Confirmed => 1.0,
        EvidenceSource.Document => 1.0,
        EvidenceSource.UserEntered => 0.9,
        EvidenceSource.Estimated => 0.7,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Nguồn bằng chứng không hợp lệ."),
    };
}
