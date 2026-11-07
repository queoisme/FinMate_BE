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

    /// <summary>Trần số tháng khi dò "bao lâu nữa đủ tiền" (50 năm); không đạt trong khoảng này thì coi là không bao giờ (provisional).</summary>
    public const int MaxSearchMonths = 600;

    /// <summary>Trần giá mục tiêu nhận từ lời gọi công cụ (đồng): đủ cho nhà ở, và đủ nhỏ để phép nhân số nguyên không tràn <c>long</c> (provisional).</summary>
    public const long MaxPriceVnd = 10_000_000_000_000;

    /// <summary>Khóa giả định tăng giá dùng khi lời gọi không chỉ định khóa nào.</summary>
    public const string DefaultGrowthAssumptionKey = "general_price_growth";

    /// <summary>Khoảng cho phép của một giả định kinh tế, tính bằng điểm cơ bản (-100% … +1000%/năm) (provisional); cùng khoảng với CHECK ở DB.</summary>
    public const int AssumptionMinRateBps = -10_000;

    public const int AssumptionMaxRateBps = 100_000;

    /// <summary>Số tháng đã trọn gần nhất được dùng để dựng dải thu nhập và chi tiêu (provisional).</summary>
    public const int HistoryMonths = 6;

    /// <summary>Dưới số tháng lịch sử này thì không dựng dải thu nhập, trả InsufficientData (provisional).</summary>
    public const int MinHistoryMonths = 3;

    /// <summary>Hệ số biến thiên thu nhập từ mức này trở lên coi là nhóm thu nhập không ổn định (provisional).</summary>
    public const double VariableSegmentCv = 0.25;

    /// <summary>Ngữ cảnh hội thoại ngắn hạn sống chừng này rồi tự hết (provisional).</summary>
    public static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);

    /// <summary>Sau chừng này lượt đã trả lời thì ngữ cảnh bắt đầu lại từ đầu: bộ nhớ ngắn hạn không được phình vô hạn (provisional).</summary>
    public const int MaxTurns = 6;

    /// <summary>Khóa bộ nhớ dài hạn được phép (sở thích do chính người dùng đặt, xem/sửa/xóa được). Khóa nào không có ở đây bị từ chối: bộ nhớ không phải chỗ chứa dữ liệu tự do (provisional).</summary>
    public static readonly IReadOnlyList<string> MemoryKeys = ["preferred_method", "eating_out_weekends", "notification_quiet_hours", "communication_tone"];

    /// <summary>Độ dài tối đa một giá trị bộ nhớ; cùng giới hạn với cột <c>agent_user_memory.value</c> ở ai-service.</summary>
    public const int MaxMemoryValueLength = 200;

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
