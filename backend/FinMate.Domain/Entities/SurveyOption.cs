namespace FinMate.Domain.Entities;

/// <summary>Một đáp án của câu hỏi khảo sát, kèm ánh xạ "đáp án này sinh ra signals nào".</summary>
public class SurveyOption
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }

    /// <summary>Mã đáp án trong câu (A–E). Bất biến.</summary>
    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>JSON object {signal_key: trọng số nguyên}. Admin chỉnh ánh xạ ở đây.</summary>
    public string Signals { get; set; } = "{}";

    /// <summary>Khoảng % thu nhập của đáp án (câu chi linh hoạt). Kế hoạch dùng điểm giữa; <see cref="ShareMaxPct"/> NULL = "từ min trở lên".</summary>
    public int? ShareMinPct { get; set; }
    public int? ShareMaxPct { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
