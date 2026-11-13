using FinMate.API.Authorization;
using FinMate.Application.Groups;
using System.Security.Claims;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Queries;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly IConfirmTransactionCommandHandler _confirmHandler;
    private readonly ICreateManualTransactionCommandHandler _createHandler;
    private readonly ICreateTransferCommandHandler _transferHandler;
    private readonly IUpdateTransactionCommandHandler _updateHandler;
    private readonly IDeleteTransactionCommandHandler _deleteHandler;
    private readonly IParseNaturalLanguageCommandHandler _parseHandler;
    private readonly IScanReceiptCommandHandler _scanReceiptHandler;
    private readonly IChatCommandHandler _chatHandler;
    private readonly IVoiceChatCommandHandler _voiceHandler;
    private readonly IGetTransactionListQueryHandler _listHandler;
    private readonly IGetTransactionDetailQueryHandler _detailHandler;

    public TransactionsController(
        IConfirmTransactionCommandHandler confirmHandler,
        ICreateManualTransactionCommandHandler createHandler,
        ICreateTransferCommandHandler transferHandler,
        IUpdateTransactionCommandHandler updateHandler,
        IDeleteTransactionCommandHandler deleteHandler,
        IParseNaturalLanguageCommandHandler parseHandler,
        IScanReceiptCommandHandler scanReceiptHandler,
        IChatCommandHandler chatHandler,
        IVoiceChatCommandHandler voiceHandler,
        IGetTransactionListQueryHandler listHandler,
        IGetTransactionDetailQueryHandler detailHandler)
    {
        _confirmHandler = confirmHandler;
        _createHandler = createHandler;
        _transferHandler = transferHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _parseHandler = parseHandler;
        _scanReceiptHandler = scanReceiptHandler;
        _chatHandler = chatHandler;
        _voiceHandler = voiceHandler;
        _listHandler = listHandler;
        _detailHandler = detailHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? categoryId,
        [FromQuery] TransactionType? type,
        [FromQuery] TransactionStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        [FromQuery] TransactionRelation? relation,
        [FromQuery] DuplicateStatus? duplicateStatus,
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetTransactionListQuery(
                CurrentUserId, accountId, categoryId, type, status, fromDate, toDate, cursor, limit, relation, duplicateStatus),
            ct);
        return Ok(ApiResponse<IReadOnlyList<TransactionDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var transaction = await _detailHandler.HandleAsync(new GetTransactionDetailQuery(CurrentUserId, id), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateManualTransactionRequest request, CancellationToken ct)
    {
        var result = await _createHandler.HandleAsync(
            new CreateManualTransactionCommand(
                CurrentUserId,
                request.FinancialAccountId,
                request.CategoryId,
                request.AmountCents,
                request.TransactionType,
                request.TransactedAt,
                request.MerchantName,
                request.Description,
                request.ClientRequestId,
                request.Source ?? TransactionSource.Manual,
                request.ReceiptScanId,
                request.ReceiptGroup,
                request.GroupId,
                request.GroupCategoryId,
                request.Relation),
            ct);
        return Created(result);
    }

    /// <summary>
    /// 201 khi thật sự tạo mới, 200 khi <c>clientRequestId</c> khớp một giao dịch đã có.
    /// Trả 201 cho một request không tạo ra gì là nói sai với client — và client nào đếm số
    /// giao dịch đã đồng bộ theo mã 201 sẽ đếm nhầm.
    /// </summary>
    private IActionResult Created(TransactionCreationResult result)
        => StatusCode(
            result.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created,
            ApiResponse<TransactionDto>.Ok(result.Transaction));

    /// <summary>
    /// Chuyển tiền giữa 2 ví của chính user (rút ATM, nạp ví điện tử). Tách khỏi POST /transactions
    /// vì nó chạm 2 số dư và KHÔNG tiêu ngân sách — xem ARCHITECTURE.md §0 quyết định #1.
    /// </summary>
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer([FromBody] CreateTransferRequest request, CancellationToken ct)
    {
        var result = await _transferHandler.HandleAsync(
            new CreateTransferCommand(
                CurrentUserId,
                request.FromAccountId,
                request.ToAccountId,
                request.AmountCents,
                request.TransactedAt,
                request.Description,
                request.ClientRequestId),
            ct);
        return Created(result);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransactionRequest request, CancellationToken ct)
    {
        var transaction = await _updateHandler.HandleAsync(
            new UpdateTransactionCommand(
                CurrentUserId,
                id,
                request.FinancialAccountId,
                request.CounterAccountId,
                request.CategoryId,
                request.AmountCents,
                request.TransactionType,
                request.TransactedAt,
                request.MerchantName,
                request.Description),
            ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _deleteHandler.HandleAsync(new DeleteTransactionCommand(CurrentUserId, id), ct);
        return NoContent();
    }

    /// <summary>
    /// Chốt một giao dịch nháp. Body KHÔNG bắt buộc: nhánh một chạm của docx bước 5.2 gửi
    /// rỗng, nhánh chọn danh mục ở bước 5.3 gửi kèm <c>categoryId</c> — vẫn đúng một lần gọi.
    /// </summary>
    /// <summary>Chia sẻ giao dịch của mình vào nhóm, chuyển sang nhóm khác, hoặc đổi danh mục trong nhóm. Ví và số dư không đổi.</summary>
    [HttpPut("{id:guid}/group")]
    [RequireGroupsEnabled]
    public async Task<IActionResult> Share(Guid id, [FromBody] ShareTransactionRequest request, [FromServices] IGroupTransactionService groupTransactions, CancellationToken ct)
    {
        await groupTransactions.ShareAsync(CurrentUserId, id, request.GroupId, request.GroupCategoryId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/group")]
    [RequireGroupsEnabled]
    public async Task<IActionResult> Unshare(Guid id, [FromServices] IGroupTransactionService groupTransactions, CancellationToken ct)
    {
        await groupTransactions.UnshareAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Ghi tay một khoản hoàn (hoặc đảo) cho khoản chi <paramref name="id"/> (Phase 81). Mặc định hoàn phần còn lại, vào ví gốc, ngày hiện tại.
    /// 201 khi tạo, 200 khi <c>clientRequestId</c> đã dùng. Khoản gốc hay ví của người khác → 404; vượt số gốc → 422 <c>REFUND_EXCEEDS_ORIGINAL</c>.
    /// </summary>
    [HttpPost("{id:guid}/refunds")]
    public async Task<IActionResult> CreateRefund(
        Guid id, [FromBody] CreateRefundRequest? request, [FromServices] ICreateRefundCommandHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new CreateRefundCommand(
                CurrentUserId, id, request?.AmountCents, request?.TransactedAt, request?.FinancialAccountId, request?.Relation, request?.ClientRequestId),
            ct);
        return Created(result);
    }

    /// <summary>Liên kết khoản tiền vào <paramref name="id"/> làm hoàn/đảo của một khoản chi (Phase 81). Áp lại tác động trong một lượt lưu.</summary>
    [HttpPut("{id:guid}/related")]
    public async Task<IActionResult> LinkRelated(
        Guid id, [FromBody] LinkRelatedTransactionRequest request, [FromServices] ILinkRelatedTransactionCommandHandler handler, CancellationToken ct)
    {
        var transaction = await handler.HandleAsync(
            new LinkRelatedTransactionCommand(CurrentUserId, id, request.RelatedTransactionId, request.Relation, request.CategoryId), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    /// <summary>Gỡ liên kết: khoản đó trở lại thành thu nhập thường.</summary>
    [HttpDelete("{id:guid}/related")]
    public async Task<IActionResult> UnlinkRelated(Guid id, [FromServices] IUnlinkRelatedTransactionCommandHandler handler, CancellationToken ct)
    {
        var transaction = await handler.HandleAsync(new UnlinkRelatedTransactionCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    /// <summary>
    /// "Không trùng" (Phase 82): giữ liên kết với trạng thái <c>Dismissed</c> để cặp này không bị gợi ý lại. Nháp sau đó xác nhận như thường.
    /// Gọi lại → 200 như cũ; giao dịch không nghi trùng → 422 <c>NOT_SUSPECTED_DUPLICATE</c>; của người khác → 404.
    /// </summary>
    [HttpPost("{id:guid}/not-duplicate")]
    public async Task<IActionResult> NotDuplicate(Guid id, [FromServices] INotDuplicateCommandHandler handler, CancellationToken ct)
    {
        var transaction = await handler.HandleAsync(new NotDuplicateCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    /// <summary>
    /// Gộp hai giao dịch là một (Phase 82). Bản giữ lại do hệ thống chọn (đã xác nhận trước, rồi bản tạo trước / bản ngân hàng báo) và luôn được
    /// xác nhận; trả về bản đó. Gộp lại cùng cặp → 200 với bản cũ. Khác chiều/số tiền, có khoản hoàn, đã chia sẻ nhóm… → 422 <c>MERGE_NOT_ALLOWED</c>;
    /// giao dịch của người khác hoặc không tồn tại → 404.
    /// </summary>
    [HttpPost("{id:guid}/merge")]
    public async Task<IActionResult> Merge(
        Guid id, [FromBody] MergeTransactionsRequest request, [FromServices] IMergeTransactionsCommandHandler handler, CancellationToken ct)
    {
        var survivor = await handler.HandleAsync(
            new MergeTransactionsCommand(CurrentUserId, id, request.WithTransactionId, request.CategoryId, request.ClientRequestId), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(survivor));
    }

    /// <summary>
    /// Đổi khoản chi/thu thành chuyển tiền giữa hai ví của chính người dùng (Phase 83). Có <c>pairedTransactionId</c> → ghép hai chiều, phần chênh thành
    /// khoản "Phí giao dịch". Gọi lại cùng input → 200 như cũ. Lỗi: 404 (giao dịch/ví của người khác), 422 <c>TRANSFER_SHARED_NOT_ALLOWED</c>,
    /// <c>TRANSFER_NOT_ALLOWED</c>, <c>TRANSFER_PAIR_MISMATCH</c>, <c>TRANSACTION_TRANSFER_SAME_ACCOUNT</c>.
    /// </summary>
    [HttpPost("{id:guid}/convert-to-transfer")]
    public async Task<IActionResult> ConvertToTransfer(
        Guid id, [FromBody] ConvertToTransferRequest request, [FromServices] IConvertToTransferCommandHandler handler, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new ConvertToTransferCommand(CurrentUserId, id, request.CounterAccountId, request.PairedTransactionId, request.ClientRequestId), ct);
        return Ok(ApiResponse<TransferConversionDto>.Ok(result));
    }

    /// <summary>"Không phải chuyển ví" (Phase 83): tắt gợi ý cho giao dịch này và chiều đang được gợi ý ghép cùng. Gọi lại → 200.</summary>
    [HttpPost("{id:guid}/not-transfer")]
    public async Task<IActionResult> NotTransfer(Guid id, [FromServices] INotTransferCommandHandler handler, CancellationToken ct)
    {
        var transaction = await handler.HandleAsync(new NotTransferCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(
        Guid id, [FromBody] ConfirmTransactionRequest? request, CancellationToken ct)
    {
        var transaction = await _confirmHandler.HandleAsync(
            new ConfirmTransactionCommand(
                CurrentUserId, id, request?.CategoryId, request?.GroupId, request?.GroupCategoryId, request?.RelatedTransactionId, request?.Relation),
            ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    /// <summary>
    /// Ảnh hóa đơn → các trường để client điền sẵn form (docx phương thức 3).
    ///
    /// Không tạo giao dịch: docx yêu cầu người dùng rà soát trước khi lưu. Client bấm Lưu thì
    /// đi qua <c>POST /transactions</c> với <c>source = Receipt</c> như bình thường.
    /// Ảnh không được lưu ở đâu trong hệ thống.
    /// </summary>
    [HttpPost("scan-receipt")]
    [RequireSubscriptionFeature(SubscriptionFeatures.ReceiptScan)]
    [RequestSizeLimit(ScanReceiptCommandHandler.MaxImageBytes + 4096)]
    public async Task<IActionResult> ScanReceipt(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageInvalid, "Vui lòng chọn ảnh hóa đơn.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var scanned = await _scanReceiptHandler.HandleAsync(
            new ScanReceiptCommand(
                CurrentUserId,
                buffer.ToArray(),
                file.FileName,
                file.ContentType ?? string.Empty),
            ct);

        return Ok(ApiResponse<ScannedReceiptDto>.Ok(scanned));
    }

    /// <summary>
    /// Một câu chat → ghi giao dịch, bản nháp để xác nhận, hoặc số liệu tổng hợp (xem <c>ChatActions</c>).
    /// Ghi thẳng chỉ khi AI Service đủ chắc và tự chọn được ví và danh mục; hoàn tác và sửa danh mục dùng
    /// <c>DELETE</c> và <c>PATCH /transactions/{id}</c>.
    /// </summary>
    [HttpPost("chat")]
    [RequireSubscriptionFeature(SubscriptionFeatures.AiChat)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        var result = await _chatHandler.HandleAsync(
            new ChatCommand(CurrentUserId, request.Text, request.FinancialAccountId, request.ClientRequestId), ct);
        return Ok(ApiResponse<ChatResultDto>.Ok(result));
    }

    /// <summary>
    /// Giọng nói → chữ (stt-service) → đúng luồng <c>POST /transactions/chat</c>. Trả <c>transcript</c> để client cho người dùng xem
    /// câu đã nghe được, kèm kết quả chat. Audio không được lưu ở đâu. Form: <c>file</c> (≤10 MiB), tùy chọn
    /// <c>financialAccountId</c>, <c>clientRequestId</c>.
    /// </summary>
    [HttpPost("voice")]
    [RequireSubscriptionFeature(SubscriptionFeatures.VoiceInput)]
    [RequestSizeLimit(VoiceChatCommandHandler.MaxAudioBytes + 4096)]
    public async Task<IActionResult> Voice(IFormFile file, [FromForm] Guid? financialAccountId, [FromForm] Guid? clientRequestId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessRuleException(TransactionErrorCodes.VoiceAudioInvalid, "Vui lòng ghi âm hoặc chọn tệp âm thanh.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var result = await _voiceHandler.HandleAsync(
            new VoiceChatCommand(CurrentUserId, buffer.ToArray(), file.FileName, file.ContentType ?? string.Empty,
                financialAccountId, clientRequestId), ct);
        return Ok(ApiResponse<VoiceChatResultDto>.Ok(result));
    }

    [HttpPost("parse")]
    [RequireSubscriptionFeature(SubscriptionFeatures.AiChat)]
    public async Task<IActionResult> Parse([FromBody] ParseNaturalLanguageRequest request, CancellationToken ct)
    {
        var parsed = await _parseHandler.HandleAsync(new ParseNaturalLanguageCommand(CurrentUserId, request.Text), ct);
        return Ok(ApiResponse<ParsedTransactionDto>.Ok(parsed));
    }
}

/// <param name="Source">
/// Kênh nhập: bỏ trống = <c>Manual</c>. Client gửi <c>Voice</c> khi người dùng đọc bằng giọng
/// nói và <c>Receipt</c> khi điền từ ảnh hóa đơn, để về sau còn đo được kênh nào hay dùng và
/// kênh nào hay bị sửa lại. <c>Notification</c> bị từ chối — xem validator.
/// </param>
public record CreateManualTransactionRequest(
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description,
    Guid? ClientRequestId = null,
    TransactionSource? Source = null,
    Guid? ReceiptScanId = null,
    string? ReceiptGroup = null,
    Guid? GroupId = null,
    Guid? GroupCategoryId = null,
    TransactionRelation? Relation = null);

/// <param name="ClientRequestId">
/// Id do client sinh để gửi lại không thành giao dịch trùng — xem <c>Transaction.ClientRequestId</c>.
/// </param>
public record CreateTransferRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    long AmountCents,
    DateTimeOffset TransactedAt,
    string? Description,
    Guid? ClientRequestId = null);

public record UpdateTransactionRequest(
    Guid FinancialAccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description);

public record CreateRefundRequest(
    long? AmountCents = null,
    DateTimeOffset? TransactedAt = null,
    Guid? FinancialAccountId = null,
    TransactionRelation? Relation = null,
    Guid? ClientRequestId = null);

public record ConfirmTransactionRequest(
    Guid? CategoryId,
    Guid? GroupId = null,
    Guid? GroupCategoryId = null,
    Guid? RelatedTransactionId = null,
    TransactionRelation? Relation = null);

/// <param name="RelatedTransactionId">Khoản chi gốc; null + <c>Refund</c> = hoàn không có khoản gốc, khi đó bắt buộc <paramref name="CategoryId"/>.</param>
public record LinkRelatedTransactionRequest(Guid? RelatedTransactionId, TransactionRelation Relation = TransactionRelation.Refund, Guid? CategoryId = null);

/// <summary>Phase 82: <c>WithTransactionId</c> là bản còn lại của cặp; <c>CategoryId</c> tuỳ chọn thắng mọi nguồn; <c>ClientRequestId</c> nhận nhưng không dùng (gộp lại cùng cặp đã trả về bản cũ).</summary>
public record MergeTransactionsRequest(Guid WithTransactionId, Guid? CategoryId = null, Guid? ClientRequestId = null);

/// <summary>Phase 83: <c>CounterAccountId</c> là ví còn lại (đích nếu giao dịch là khoản chi, nguồn nếu là khoản thu).</summary>
public record ConvertToTransferRequest(Guid CounterAccountId, Guid? PairedTransactionId = null, Guid? ClientRequestId = null);

public record ShareTransactionRequest(Guid GroupId, Guid? GroupCategoryId);
public record ParseNaturalLanguageRequest(string Text);

public record ChatRequest(string Text, Guid? FinancialAccountId = null, Guid? ClientRequestId = null);
