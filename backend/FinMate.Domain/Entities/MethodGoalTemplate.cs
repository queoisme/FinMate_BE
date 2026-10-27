namespace FinMate.Domain.Entities;

/// <summary>Phương pháp nào gợi ý mẫu mục tiêu nào, và khi nào — đây là "mục tiêu gắn với từng phương pháp".</summary>
public class MethodGoalTemplate
{
    public Guid MethodId { get; set; }
    public Guid TemplateId { get; set; }

    /// <summary>JSON object <c>{"excludes":[signal]}</c>: không gợi ý mẫu này khi người dùng có một trong các signal đó (vd đang thiếu hụt dòng tiền). NULL = luôn gợi ý.</summary>
    public string? Conditions { get; set; }

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
