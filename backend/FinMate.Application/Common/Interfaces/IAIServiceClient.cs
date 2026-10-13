namespace FinMate.Application.Common.Interfaces;

// Record types khớp field-cho-field với contract Backend <-> AI Service ở
// ARCHITECTURE.md §3.3. UserId ở đây là Guid thật — việc băm SHA-256 thành
// user_id_hash trước khi gửi qua HTTP là trách nhiệm của Infrastructure
// (AGENTS.md §3.1: AI DB không lưu user_id thực), Application không cần biết.
public record AnalyzeRequest(
    Guid BackendRequestId,
    Guid UserId,
    string PackageName,
    string? NotificationTitle,
    string NotificationBody,
    DateTimeOffset ReceivedAt);

public record ClassifierResult(string Label, double Confidence);

public record ExtractionResult(
    long? AmountCents,
    string? TransactionType,
    string? MerchantName,
    string? Description,
    DateTimeOffset? TransactedAt,
    long? BalanceAfterCents,
    double? Confidence);

public record CategorizationResult(string? CategorySlug, double? Confidence);

public record DuplicateResult(bool IsPotentialDuplicate, Guid? DuplicateRequestId);

public record ModelVersions(string? Classifier, string? Extractor, string? Categorizer);

public record AnalyzeResponse(
    string PipelineResult,
    ClassifierResult Classifier,
    ExtractionResult? Extraction,
    CategorizationResult? Categorization,
    DuplicateResult? Duplicate,
    ModelVersions? ModelVersions,
    int? ProcessingMs);

// TransactionId/UserId ở đây là Guid thật — băm SHA-256 trước khi gửi qua HTTP cũng là
// trách nhiệm của Infrastructure, giống AnalyzeRequest.UserId ở trên.
public record FeedbackRequest(
    Guid TransactionId,
    Guid UserId,
    Guid? PipelineRequestId,
    string PackageName,
    string? PredictedCategory,
    string? CorrectedCategory,
    string FeedbackType);

// GET /api/v1/stats — route thứ ba của contract, thêm ở Phase 8 cho màn hình quản trị.
// Toàn bộ là số đếm ở mức hệ thống; không có gì gắn với một người dùng cụ thể.
public record AiModelStatus(
    string Stage,
    string? Version,
    DateTimeOffset? TrainedAt,
    double? Accuracy,
    double? MacroF1,
    string? EvaluatedOnSplit);

public record AiTrainingJobStatus(
    string Stage,
    string Status,
    int? SampleCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorMessage);

public record AiServiceStats(
    IReadOnlyList<AiModelStatus> Models,
    int RawSampleCount,
    int LabeledSampleCount,
    int UnlabeledSampleCount,
    IReadOnlyDictionary<string, int> SplitCounts,
    AiTrainingJobStatus? LastTrainingJob,
    int PendingFeedbackCount);

// POST /api/v1/ocr — route thứ tư của contract, thêm ở Phase 10 (docx phương thức 3).
// Ảnh chỉ đi qua, không được lưu ở đâu trong hệ thống.
public record ScanReceiptRequest(
    Guid UserId,
    byte[] Image,
    string FileName,
    string ContentType);

/// <param name="OcrResult">"success" | "no_amount" | "unreadable".</param>
public record ScanReceiptResponse(
    string OcrResult,
    ExtractionResult? Extraction,
    CategorizationResult? Categorization,
    int? ProcessingMs);

// POST /api/v1/chat/parse — route thứ năm của contract, thêm ở Phase 20. AI Service chỉ HIỂU câu chat và
// trả quyết định; backend lưu giao dịch và tự tính tổng. AmountCents là đồng VND như mọi nơi khác.
public record ChatParseRequest(Guid UserId, string Text);

/// <param name="Kind">"expense" | "income" | "transfer".</param>
/// <param name="CategorySlug">Chi tiêu: slug hệ thống hoặc null nếu AI chưa đoán được. Thu nhập luôn "income". Chuyển tiền: null.</param>
/// <param name="IncomeType">salary | bonus | gift | side_income | refund | other_income, khi đoán được.</param>
/// <param name="AutoSaveOk">AI đủ chắc để ghi ngay: chi tiêu đã biết danh mục, dưới ngưỡng tiền, Rasa (nếu có) chắc.</param>
/// <param name="ToOthers">"Chuyển cho người khác / trả nợ" đã được AI tính là chi tiêu.</param>
/// <param name="CategorySource">rule (từ khóa quyết định) | cache (chỉ gợi ý từ bảng nhớ tên món) | null (chưa biết).</param>
public record ChatParseEntry(
    string Kind,
    long AmountCents,
    string? Description,
    string? MerchantName,
    string? CategorySlug,
    string? IncomeType,
    bool AutoSaveOk,
    bool ToOthers,
    DateTimeOffset? OccurredAt = null,
    string? CategorySource = null);

/// <param name="Metric">"expense" | "income" | "balance".</param>
/// <param name="From">Đầu kỳ (giờ VN).</param>
/// <param name="To">Cuối kỳ, nửa mở: [From, To).</param>
public record ChatParseQuery(
    string Metric,
    string PeriodLabel,
    DateTimeOffset From,
    DateTimeOffset To,
    string? CategorySlug,
    string? IncomeType);

/// <param name="Intent">add_expense | add_income | transfer_money | ask_spending | greet | goodbye | thanks | confirm | deny | cancel | out_of_scope | needs_amount | empty.</param>
public record ChatParseResponse(
    string Intent,
    string? ReplyText,
    ChatParseEntry? Entry,
    ChatParseQuery? Query,
    int? ProcessingMs,
    IReadOnlyList<ChatParseEntry>? Entries = null,
    long? TotalAmount = null,
    bool TotalMismatch = false,
    IReadOnlyList<string>? Skipped = null);

// POST /api/v1/bill/classify — route thứ sáu của contract, thêm ở Phase 21 (bước 3 của luồng ảnh → OCR → lọc → AI phân loại).
// Chỉ nhận diện và phân loại chữ ĐÃ lọc; không lưu gì.
/// <param name="MerchantName">Tên cửa hàng OCR đọc ra, có thể null.</param>
/// <param name="Lines">Chữ đã lọc dữ liệu cá nhân.</param>
/// AI Service tự tra cửa hàng người dùng đã chốt (theo hash của <c>UserId</c>) nên backend không phải gửi gợi ý.
public record BillClassifyRequest(Guid UserId, string? MerchantName, IReadOnlyList<string> Lines);

/// <param name="CategorySlug">Slug hệ thống, hoặc null khi AI không nhận ra (không ghi "other" cho có).</param>
/// <param name="LocalCategory">9 nhóm của bộ luật bill (giữ phân biệt Ăn uống / Siêu thị).</param>
/// <param name="Source">memory (người dùng đã chốt cho cửa hàng này) | brand | keyword | product | model | none.</param>
public record BillClassifyResponse(string? CategorySlug, string? LocalCategory, double Confidence, string Source);

// POST /api/v1/bill/learn — route thứ bảy của contract, thêm ở Phase 21. Người dùng đã chọn <c>CategorySlug</c> (slug hệ thống) cho cửa hàng này khi
// lưu một khoản chi từ hóa đơn; lần quét sau AI Service gợi ý đúng danh mục đó cho người dùng đó. Chỉ tên cửa hàng và slug, không chữ OCR.
public record LearnMerchantRequest(Guid UserId, string MerchantName, string CategorySlug);

// POST /api/v1/bill/classify-items — phân loại từng món. KHÔNG bao giờ gửi số tiền, chỉ tên món và chỉ số dòng cha.
public record ClassifyItemInput(string Name, int? Parent);

public record ClassifyItemsRequest(Guid UserId, string? MerchantName, IReadOnlyList<ClassifyItemInput> Items, bool AllowAi);

/// <param name="Source">rule | cache | inherit | merchant | ai | unknown.</param>
public record ClassifiedItem(int Index, string? CategorySlug, string Source);

public record ClassifyItemsResponse(IReadOnlyList<ClassifiedItem> Items, bool AiUsed);

public interface IAIServiceClient
{
    Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default);

    // Best-effort — lỗi không được chặn luồng chính (UpdateTransactionCommand).
    Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default);

    /// <summary>
    /// Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản
    /// hồi. Caller (màn hình quản trị) bắt lấy và vẫn hiển thị phần số liệu của backend —
    /// một dashboard không được sập chỉ vì AI Service đang restart.
    /// </summary>
    Task<AiServiceStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản
    /// hồi — khác hẳn với "ảnh không đọc được", cái sau trả về bình thường kèm
    /// <c>OcrResult = "unreadable"</c>. Người dùng cần phân biệt "chụp lại đi" với "thử lại sau".
    /// </summary>
    Task<ScanReceiptResponse> ScanReceiptAsync(
        ScanReceiptRequest request, CancellationToken ct = default);

    /// <summary>
    /// Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản hồi.
    /// Rasa tắt thì AI Service vẫn trả lời (bằng luật), nên lỗi ở đây là lỗi của chính AI Service.
    /// </summary>
    Task<ChatParseResponse> ParseChatAsync(ChatParseRequest request, CancellationToken ct = default);

    /// <summary>Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản hồi.</summary>
    Task<BillClassifyResponse> ClassifyBillAsync(BillClassifyRequest request, CancellationToken ct = default);

    /// <summary>Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi AI Service không phản hồi.</summary>
    Task<ClassifyItemsResponse> ClassifyItemsAsync(ClassifyItemsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Best-effort, như <see cref="SendFeedbackAsync"/>: tự nuốt lỗi và giới hạn thời gian chờ, vì nó chạy ngay sau khi giao dịch đã lưu
    /// và không được làm chậm hay làm hỏng việc lưu.
    /// </summary>
    Task LearnMerchantAsync(LearnMerchantRequest request, CancellationToken ct = default);
}
