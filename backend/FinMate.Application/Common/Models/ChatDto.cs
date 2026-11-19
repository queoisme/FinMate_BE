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

    /// <summary>Tạo ngân sách / khoản định kỳ / khoản vay nợ: <see cref="ChatResultDto.Item"/> đã đủ thông tin, client tự gọi API tạo (có nút Hoàn tác). Thiếu thông tin thì là <see cref="Reply"/> với câu hỏi lại.</summary>
    public const string Item = "item";

    /// <summary>Hỏi "mua món này có ổn không": client hiện <see cref="ChatResultDto.Purchase"/> rồi gọi <c>POST /forecast/purchase-check</c> để lấy đánh giá. Không tạo giao dịch.</summary>
    public const string Purchase = "purchase";
}

/// <summary>Khoản mua người dùng đang cân nhắc, từ câu chat. Chưa có kết luận: kết quả tính ở <c>POST /forecast/purchase-check</c>.</summary>
/// <param name="PurchaseDate">Ngày dự định mua; null = hôm nay.</param>
public record ChatPurchaseDraftDto(string? Item, long AmountCents, DateOnly? PurchaseDate, string? CategorySlug);

/// <summary>Ngân sách / khoản định kỳ / khoản vay nợ từ câu chat, đủ thông tin. Chưa tạo gì ở backend: client gọi <c>POST /budgets</c>, <c>/recurring-items</c> hoặc <c>/loans</c>.</summary>
/// <param name="Kind">budget | recurring | loan.</param>
/// <param name="AmountCents">Đồng VND: hạn mức tháng / số tiền mỗi lần / số tiền vay.</param>
/// <param name="Name">recurring: tên khoản; loan: tiêu đề.</param>
/// <param name="Cadence">recurring: weekly | monthly | yearly.</param>
/// <param name="Direction">loan: borrow (mình đi vay) | lend (mình cho vay).</param>
public record ChatItemDraftDto(string Kind, long AmountCents, string? Name, string? CategorySlug, string? Cadence, DateOnly? NextDue, string? Direction, string? Counterparty);

/// <summary>Bản nháp mục tiêu tiết kiệm từ câu chat. Chưa tạo gì: tạo bằng <c>POST /saving-goals</c> sau khi người dùng xác nhận.</summary>
/// <param name="NameIsDefault">Người dùng không nói tên nên <paramref name="Name"/> là tên mặc định; client cho sửa.</param>
/// <param name="Deadline">Chốt 23:59:59 giờ VN của ngày hạn; null = không nói hạn.</param>
/// <param name="DeadlineText">Cụm người dùng nói ("2 tháng sau"), để hiện kèm ngày cụ thể.</param>
/// <param name="AiAssisted">Hạn hoặc tên do AI đọc hộ: app không tự tạo mục tiêu, để người dùng soát rồi bấm tạo.</param>
public record ChatGoalDraftDto(string Name, bool NameIsDefault, long TargetCents, DateTimeOffset? Deadline, string? DeadlineText, bool AiAssisted = false);

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
/// <param name="RelationHint">Phase 81 (D17): <c>Refund</c> khi câu chat là nhận tiền hoàn; app cho chọn khoản chi gốc rồi gọi <c>POST /transactions/{id}/refunds</c>.</param>
/// <param name="SuggestedRelatedTransactions">Tối đa 3 khoản chi gốc gợi ý khi có <paramref name="RelationHint"/>.</param>
/// <param name="PossibleDuplicateTransactionId">Giữ cho app cũ: khoản cùng số tiền trong ±10 phút, hoặc ứng viên trùng hạng 1.</param>
/// <param name="SuggestedDuplicates">Phase 82: tối đa 3 giao dịch đã ghi mà khoản này có thể trùng (hạng 1 trước). Lưu khoản này thì nó mang nghi trùng.</param>
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
    Guid? PossibleDuplicateTransactionId = null,
    string? RelationHint = null,
    IReadOnlyList<RefundSuggestionDto>? SuggestedRelatedTransactions = null,
    IReadOnlyList<DuplicateSummaryDto>? SuggestedDuplicates = null,
    SourceDetailDto? SourceDetail = null,
    string? AiExplanation = null,
    DraftConfidenceDto? Confidence = null);

public record ChatSummaryItemDto(string? CategorySlug, string CategoryName, long AmountCents, double Share);

/// <summary>Một khoản trong danh sách chi tiết của kỳ (khi người dùng hỏi "chi tiết hôm nay", "liệt kê các khoản chi tháng này").</summary>
/// <param name="Type">"expense" | "income".</param>
public record ChatTransactionDto(
    Guid Id,
    DateTimeOffset TransactedAt,
    long AmountCents,
    string Type,
    string? MerchantName,
    string? Description,
    string? CategorySlug,
    string? CategoryName);

/// <param name="Metric">"expense" | "income" | "balance".</param>
/// <param name="Items">Theo danh mục khi Metric = expense; rỗng với income và balance.</param>
/// <param name="Transactions">Danh sách từng khoản của kỳ, mới nhất trước, CHỈ khi người dùng xin chi tiết (null = không xin). Có giới hạn số dòng: xem <paramref name="TransactionsHasMore"/>.</param>
/// <param name="TransactionsHasMore">Còn khoản cũ hơn chưa hiện (vượt giới hạn): app mời xem tiếp ở màn hình giao dịch.</param>
public record ChatSummaryDto(
    string Metric,
    string PeriodLabel,
    DateTimeOffset From,
    DateTimeOffset To,
    long TotalCents,
    long IncomeCents,
    long ExpenseCents,
    long LeftCents,
    IReadOnlyList<ChatSummaryItemDto> Items,
    IReadOnlyList<ChatTransactionDto>? Transactions = null,
    bool TransactionsHasMore = false);

/// <param name="Intent">Ý định AI Service hiểu được (add_expense, ask_spending, greet...).</param>
/// <param name="ReceivedAt">Lúc backend nhận tin của người dùng (UTC). App dùng làm giờ của bong bóng người dùng và nhãn ngày ("Hôm nay", "Hôm qua") theo múi giờ máy; luôn có trong phản hồi của <c>POST /transactions/chat</c> và <c>/transactions/voice</c>.</param>
/// <param name="RespondedAt">Lúc backend trả lời xong (UTC): giờ của bong bóng trả lời. Luôn ≥ <paramref name="ReceivedAt"/>.</param>
public record ChatResultDto(
    string Action,
    string Intent,
    string? ReplyText,
    TransactionDto? Transaction,
    ChatDraftDto? Draft,
    ChatSummaryDto? Summary,
    IReadOnlyList<ChatDraftDto>? Drafts = null,
    ChatGoalDraftDto? Goal = null,
    ChatPurchaseDraftDto? Purchase = null,
    DateTimeOffset? ReceivedAt = null,
    DateTimeOffset? RespondedAt = null,
    ChatItemDraftDto? Item = null);
