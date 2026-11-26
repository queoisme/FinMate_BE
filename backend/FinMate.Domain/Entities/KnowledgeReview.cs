namespace FinMate.Domain.Entities;

public enum KnowledgeReviewStatus
{
    Draft,
    Approved,
    Rejected,
    Retired,
}

/// <summary>
/// Phase 95: ai soạn và ai duyệt một tài liệu tri thức. Tài liệu nằm ở AI Service (không FK, chỉ trùng id); AI Service không nhận id người soạn, nên backend
/// tự ghi ở đây để chặn tự duyệt. <see cref="SubmittedBy"/> null = tài liệu tạo trước mốc <see cref="KnowledgeReviewCutover"/> (không biết người soạn).
/// </summary>
public class KnowledgeReview
{
    public Guid DocumentId { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public KnowledgeReviewStatus Status { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Lý do từ chối. Không vào audit.</summary>
    public string? DecisionNote { get; set; }
}

/// <summary>Một dòng duy nhất: thời điểm backend bắt đầu ghi người soạn (giờ chạy migration). Tài liệu tạo ở AI Service trước mốc này mới được coi là "cũ" (S3).</summary>
public class KnowledgeReviewCutover
{
    public int Id { get; set; }
    public DateTimeOffset TrackedSince { get; set; }
}
