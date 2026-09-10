using System.Security.Claims;
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
    private readonly IUpdateTransactionCommandHandler _updateHandler;
    private readonly IDeleteTransactionCommandHandler _deleteHandler;
    private readonly IParseNaturalLanguageCommandHandler _parseHandler;
    private readonly IGetTransactionListQueryHandler _listHandler;
    private readonly IGetTransactionDetailQueryHandler _detailHandler;

    public TransactionsController(
        IConfirmTransactionCommandHandler confirmHandler,
        ICreateManualTransactionCommandHandler createHandler,
        IUpdateTransactionCommandHandler updateHandler,
        IDeleteTransactionCommandHandler deleteHandler,
        IParseNaturalLanguageCommandHandler parseHandler,
        IGetTransactionListQueryHandler listHandler,
        IGetTransactionDetailQueryHandler detailHandler)
    {
        _confirmHandler = confirmHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _parseHandler = parseHandler;
        _listHandler = listHandler;
        _detailHandler = detailHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? categoryId,
        [FromQuery] TransactionType? type,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetTransactionListQuery(CurrentUserId, accountId, categoryId, type, fromDate, toDate, cursor, limit), ct);
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
        var transaction = await _createHandler.HandleAsync(
            new CreateManualTransactionCommand(
                CurrentUserId,
                request.FinancialAccountId,
                request.CategoryId,
                request.AmountCents,
                request.TransactionType,
                request.TransactedAt,
                request.MerchantName,
                request.Description),
            ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<TransactionDto>.Ok(transaction));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransactionRequest request, CancellationToken ct)
    {
        var transaction = await _updateHandler.HandleAsync(
            new UpdateTransactionCommand(
                CurrentUserId,
                id,
                request.FinancialAccountId,
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

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var transaction = await _confirmHandler.HandleAsync(new ConfirmTransactionCommand(CurrentUserId, id), ct);
        return Ok(ApiResponse<TransactionDto>.Ok(transaction));
    }

    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] ParseNaturalLanguageRequest request, CancellationToken ct)
    {
        var parsed = await _parseHandler.HandleAsync(new ParseNaturalLanguageCommand(CurrentUserId, request.Text), ct);
        return Ok(ApiResponse<ParsedTransactionDto>.Ok(parsed));
    }
}

public record CreateManualTransactionRequest(
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description);

public record UpdateTransactionRequest(
    Guid FinancialAccountId,
    Guid? CategoryId,
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    string? MerchantName,
    string? Description);

public record ParseNaturalLanguageRequest(string Text);
