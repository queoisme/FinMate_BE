namespace FinMate.Domain.Enums;

/// <summary>Người dùng làm gì với một gợi ý. <see cref="Shown"/> = chưa quyết định.</summary>
public enum RecommendationOutcome
{
    Shown,
    Accepted,
    Changed,
    Skipped,
}
