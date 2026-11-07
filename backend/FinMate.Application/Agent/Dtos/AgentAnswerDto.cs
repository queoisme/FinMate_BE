namespace FinMate.Application.Agent;

public static class AgentStatus
{
    public const string Answered = "answered";
    public const string Clarify = "clarify";
    public const string Refused = "refused";
    public const string InsufficientData = "insufficient_data";
}

public sealed record AgentClaimDto(string Text, IReadOnlyList<string> EvidenceIds);

public sealed record AgentEvidenceDto(string Id, string Key, long? ValueVnd, string? Text, string Unit, string Source, DateTimeOffset AsOf, double Confidence, string? SourceName, string? SourceUrl)
{
    public static AgentEvidenceDto From(Evidence e)
        => new(e.Id, e.Key, e.ValueVnd, e.Text, e.Unit, WireSource(e.Source), e.AsOf, Math.Round(e.Confidence, 2), e.SourceName, e.SourceUrl);

    /// <summary>Tên nguồn trên dây (JSON): cùng bộ giá trị với ai-service (<c>confirmed</c>, <c>user_entered</c>, <c>estimated</c>, <c>document</c>).</summary>
    public static string WireSource(EvidenceSource source) => source switch
    {
        EvidenceSource.Confirmed => "confirmed",
        EvidenceSource.UserEntered => "user_entered",
        EvidenceSource.Estimated => "estimated",
        EvidenceSource.Document => "document",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Nguồn bằng chứng không hợp lệ."),
    };
}

/// <param name="Status"><see cref="AgentStatus"/>.</param>
/// <param name="Message">Câu từ chối, câu hỏi lại, hoặc lý do thiếu dữ liệu; null khi <c>answered</c>.</param>
/// <param name="Evidence">Toàn bộ bằng chứng của lượt này (để màn hình hiện "Vì sao?" kèm nguồn, ngày cập nhật, độ tin cậy).</param>
/// <param name="Verdict"><c>feasible</c>, <c>tight</c>, <c>infeasible</c>; chỉ có với câu hỏi mục tiêu.</param>
/// <param name="UsedTemplate">true = câu trả lời dựng bằng mẫu cố định từ bằng chứng (ai-service lỗi hoặc không qua kiểm chứng).</param>
public sealed record AgentAnswerDto(
    string Status,
    string? Message,
    IReadOnlyList<AgentClaimDto> Claims,
    IReadOnlyList<AgentEvidenceDto> Evidence,
    string? Verdict,
    Guid TraceId,
    bool UsedTemplate);
