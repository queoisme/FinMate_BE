using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Nhật ký mỗi lần hệ thống gợi ý phương pháp: gợi ý gì, từ signals nào, do AI hay rule, prompt/phương
/// pháp phiên bản nào, và người dùng đã làm gì. Dùng để đo tỷ lệ chấp nhận và truy ngược "vì sao gợi ý cái này".
/// Không lưu nguyên văn phản hồi của AI — chỉ phần lý do đã được kiểm.
/// </summary>
public class MethodRecommendation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PrimaryMethodId { get; set; }
    public Guid? SupportMethodId { get; set; }

    /// <summary>JSON array các lý do hiển thị cho người dùng.</summary>
    public string Reasons { get; set; } = "[]";

    /// <summary>JSON object signals tại thời điểm gợi ý (không chứa định danh).</summary>
    public string SignalsSnapshot { get; set; } = "{}";

    public RecommendationSource Source { get; set; }

    /// <summary>Phiên bản prompt; NULL khi nguồn là <see cref="RecommendationSource.RuleFallback"/> (không gọi AI).</summary>
    public string? PromptVersion { get; set; }

    /// <summary>JSON object {method_code: version} của các phương pháp trong shortlist lúc đó.</summary>
    public string MethodVersions { get; set; } = "{}";

    public RecommendationOutcome Outcome { get; set; } = RecommendationOutcome.Shown;
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
