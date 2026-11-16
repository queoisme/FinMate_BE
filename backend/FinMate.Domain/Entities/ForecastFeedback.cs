namespace FinMate.Domain.Entities;

/// <summary>
/// Người dùng báo "dự báo sai". Gắn với bản chụp dự báo gần nhất của họ để Staff thấy con số và kết quả thật mà không cần xem giao dịch.
/// Staff gán <see cref="Cause"/>; Manager xem tổng hợp theo nguyên nhân cùng chỉ số độ chính xác.
/// </summary>
public class ForecastFeedback
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? SnapshotId { get; set; }

    /// <summary>Một trong <see cref="ForecastFeedbackReasons"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Lời nhắn ngắn của người dùng (tùy chọn). Có thể chứa thông tin cá nhân, chỉ Staff đọc.</summary>
    public string? Note { get; set; }

    /// <summary><c>new</c> hoặc <c>labeled</c>.</summary>
    public string Status { get; set; } = ForecastFeedbackStatuses.New;

    /// <summary>Một trong <see cref="ForecastFeedbackCauses"/>; null khi chưa gán.</summary>
    public string? Cause { get; set; }

    public Guid? LabeledByUserId { get; set; }

    public DateTimeOffset? LabeledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }

    public ForecastSnapshot? Snapshot { get; set; }
}

public static class ForecastFeedbackStatuses
{
    public const string New = "new";
    public const string Labeled = "labeled";
}

/// <summary>Lý do người dùng chọn khi báo sai.</summary>
public static class ForecastFeedbackReasons
{
    public const string BalanceWrong = "balance_wrong";
    public const string MissingBill = "missing_bill";
    public const string WrongBill = "wrong_bill";
    public const string UnusualSpending = "unusual_spending";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [BalanceWrong, MissingBill, WrongBill, UnusualSpending, Other];
}

/// <summary>Nguyên nhân do Staff gán (tài liệu SRS: thiếu dữ liệu, khoản bất thường, định kỳ bị nhận sai, lỗi mô hình).</summary>
public static class ForecastFeedbackCauses
{
    public const string DataMissing = "data_missing";
    public const string RecurringMisread = "recurring_misread";
    public const string OneOff = "one_off";
    public const string ModelIssue = "model_issue";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [DataMissing, RecurringMisread, OneOff, ModelIssue, Other];
}
