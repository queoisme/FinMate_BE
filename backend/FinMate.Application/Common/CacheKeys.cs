namespace FinMate.Application.Common;

public static class CacheKeys
{
    public static string UserProfile(Guid userId) => $"user:{userId}:profile";

    public static readonly TimeSpan UserProfileTtl = TimeSpan.FromMinutes(15);

    public static string BudgetSummary(Guid userId, int year, int month)
        => $"user:{userId}:budget_summary:{year}:{month}";

    public static readonly TimeSpan BudgetSummaryTtl = TimeSpan.FromMinutes(5);

    /// <summary>Thói quen chi tiêu dùng cho dự báo, tính từ lịch sử KHÔNG gồm hôm nay nên chỉ đổi khi sang ngày mới hoặc khi user sửa dữ liệu dự báo.</summary>
    public static string ForecastBaseline(Guid userId, DateOnly date) => $"user:{userId}:forecast_baseline:{date:yyyy-MM-dd}";

    /// <summary>Ngữ cảnh hội thoại ngắn hạn của agent (các "ô" có cấu trúc của lượt trước, không có văn bản thô). TTL: <c>AgentLimits.ConversationTtl</c>.</summary>
    public static string AgentConversation(Guid userId) => $"user:{userId}:agent_conversation";

    public static readonly TimeSpan ForecastBaselineTtl = TimeSpan.FromHours(24);
}
