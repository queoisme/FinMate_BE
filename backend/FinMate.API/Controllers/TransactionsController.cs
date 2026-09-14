using System.Security.Claims;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
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
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetTransactionListQuery(
                CurrentUserId, accountId, categoryId, type, status, fromDate, toDate, cursor, limit),
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
                request.Source ?? TransactionSource.Manual),
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
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(
        Guid id, [FromBody] ConfirmTransactionRequest? request, CancellationToken ct)
    {
        var transaction = await _confirmHandler.HandleAsync(
            new ConfirmTransactionCommand(CurrentUserId, id, request?.CategoryId), ct);
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

    [HttpPost("parse")]
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
    TransactionSource? Source = null);

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

public record ConfirmTransactionRequest(Guid? CategoryId);
public record ParseNaturalLanguageRequest(string Text);
