using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một cảnh báo dự báo trong hộp thư (outbox): chống lặp theo <c>(user, kind, dedup_key)</c>, hoãn được tới hết giờ yên lặng, lưu lại để app
/// xem chi tiết và để user báo "không chính xác". <see cref="Data"/> là payload JSON cho app — mang số tiền nên không bao giờ được ghi log.
/// </summary>
public class ForecastAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ForecastAlertKind Kind { get; set; }
    public string DedupKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Data { get; set; } = "{}";
    public ForecastAlertStatus Status { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Hiện chỉ có "không chính xác" (true); null = chưa phản hồi.</summary>
    public bool? MarkedInaccurate { get; set; }
    public DateTimeOffset? FeedbackAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
