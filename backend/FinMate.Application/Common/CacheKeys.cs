namespace FinMate.Application.Common;

public static class CacheKeys
{
    public static string UserProfile(Guid userId) => $"user:{userId}:profile";

    public static readonly TimeSpan UserProfileTtl = TimeSpan.FromMinutes(15);

    public static string BudgetSummary(Guid userId, int year, int month)
        => $"user:{userId}:budget_summary:{year}:{month}";

    public static readonly TimeSpan BudgetSummaryTtl = TimeSpan.FromMinutes(5);
}
