namespace FinMate.Application.Common.Models;

/// <summary>Giá trị của <see cref="ChatResultDto.Action"/> — client rẽ nhánh theo đây.</summary>
public static class ChatActions
{
    /// <summary>Đã ghi giao dịch (AI đủ chắc). Client hiện thẻ kèm Hoàn tác / Sửa danh mục.</summary>
    public const string Saved = "saved";

    /// <summary>Chưa ghi: client hiện <see cref="ChatResultDto.Draft"/> cho người dùng xác nhận hoặc chọn thêm.</summary>
    public const string Confirm = "confirm";

    /// <summary>Câu hỏi tổng: <see cref="ChatResultDto.Summary"/> đã tính từ giao dịch của chính người dùng.</summary>
    public const string Summary = "summary";

    /// <summary>Chào hỏi, ngoài chủ đề, thiếu số tiền...: chỉ có <see cref="ChatResultDto.ReplyText"/>.</summary>
    public const string Reply = "reply";

    /// <summary>Đặt mục tiêu tiết kiệm: client hiện <see cref="ChatResultDto.Goal"/> để xác nhận rồi tự gọi <c>POST /saving-goals</c>. Không bao giờ tự tạo.</summary>
    public const string Goal = "goal";
}

/// <summary>Bản nháp mục tiêu tiết kiệm từ câu chat. Chưa tạo gì: tạo bằng <c>POST /saving-goals</c> sau khi người dùng xác nhận.</summary>
/// <param name="NameIsDefault">Người dùng không nói tên nên <paramref name="Name"/> là tên mặc định; client cho sửa.</param>
/// <param name="Deadline">Chốt 23:59:59 giờ VN của ngày hạn; null = không nói hạn.</param>
/// <param name="DeadlineText">Cụm người dùng nói ("2 tháng sau"), để hiện kèm ngày cụ thể.</param>
public record ChatGoalDraftDto(string Name, bool NameIsDefault, long TargetCents, DateTimeOffset? Deadline, string? DeadlineText);

/// <summary>Trường còn thiếu trong <see cref="ChatDraftDto.MissingFields"/>.</summary>
public static class ChatMissingFields
{
    public const string Category = "category";
    public const string Account = "account";
    public const string Accounts = "accounts";
}

/// <param name="Kind">"expense" | "income" | "transfer".</param>
/// <param name="TransactionType">"debit" | "credit"; null với chuyển tiền (cần hai ví).</param>
/// <param name="IncomeType">Loại thu nhập AI đoán được (lương, thưởng...); backend chỉ có một danh mục "income".</param>
/// <param name="SuggestedFinancialAccountId">Ví đã tự chọn được (ví duy nhất của người dùng); null thì client hỏi.</param>
/// <param name="MissingFields">"category" | "account" | "accounts" — việc client còn phải làm trước khi gọi POST /transactions.</param>
public record ChatDraftDto(
    string Kind,
    long AmountCents,
    string? TransactionType,
    string? MerchantName,
    string? Description,
    string? CategorySlug,
    string? IncomeType,
    bool ToOthers,
    Guid? SuggestedFinancialAccountId,
    IReadOnlyList<string> MissingFields,
    DateTimeOffset? OccurredAt = null,
    Guid? PossibleDuplicateTransactionId = null);

public record ChatSummaryItemDto(string? CategorySlug, string CategoryName, long AmountCents, double Share);

/// <param name="Metric">"expense" | "income" | "balance".</param>
/// <param name="Items">Theo danh mục khi Metric = expense; rỗng với income và balance.</param>
public record ChatSummaryDto(
    string Metric,
    string PeriodLabel,
    DateTimeOffset From,
    DateTimeOffset To,
    long TotalCents,
    long IncomeCents,
    long ExpenseCents,
    long LeftCents,
    IReadOnlyList<ChatSummaryItemDto> Items);

/// <param name="Intent">Ý định AI Service hiểu được (add_expense, ask_spending, greet...).</param>
public record ChatResultDto(
    string Action,
    string Intent,
    string? ReplyText,
    TransactionDto? Transaction,
    ChatDraftDto? Draft,
    ChatSummaryDto? Summary,
    IReadOnlyList<ChatDraftDto>? Drafts = null,
    ChatGoalDraftDto? Goal = null);
