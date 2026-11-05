using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>Chỉ đọc, trừ tạo giao dịch nháp. Khách luôn được xác định qua ticket, không có tham số user id.</summary>
[Route("api/v1/ops/tickets/{ticketId:guid}")]
public class OpsCustomerDataController : OpsControllerBase
{
    private readonly IOpsCustomerDataService _data;

    public OpsCustomerDataController(IOpsCustomerDataService data)
    {
        _data = data;
    }

    [HttpPost("access-requests")]
    public async Task<IActionResult> RequestAccess(Guid ticketId, [FromBody] OpsAccessRequest request, CancellationToken ct)
    {
        var scopes = (request.Scopes ?? []).Aggregate(SupportAccessScope.None, (all, scope) => all | scope);
        return Ok(ApiResponse<SupportAccessRequestDto>.Ok(await _data.RequestAccessAsync(CurrentActorId, ticketId, scopes, request.Reason, CurrentIpAddress, ct)));
    }

    [HttpGet("access-requests")]
    public async Task<IActionResult> ListRequests(Guid ticketId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SupportAccessRequestDto>>.Ok(await _data.ListRequestsAsync(CurrentActorId, ticketId, ct)));

    [HttpGet("customer-data/accounts")]
    public async Task<IActionResult> Accounts(Guid ticketId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CustomerAccountDto>>.Ok(await _data.GetAccountsAsync(CurrentActorId, ticketId, CurrentIpAddress, ct)));

    [HttpGet("customer-data/transactions")]
    public async Task<IActionResult> Transactions(
        Guid ticketId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(ApiResponse<CustomerTransactionPageDto>.Ok(await _data.GetTransactionsAsync(CurrentActorId, ticketId, from, to, cursor, limit, CurrentIpAddress, ct)));

    [HttpGet("customer-data/budgets")]
    public async Task<IActionResult> Budgets(Guid ticketId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CustomerBudgetDto>>.Ok(await _data.GetBudgetsAsync(CurrentActorId, ticketId, CurrentIpAddress, ct)));

    /// <summary>Gửi lại cùng <c>clientRequestId</c> thì nhận lại giao dịch cũ với 200, không tạo thêm.</summary>
    [HttpPost("draft-transactions")]
    public async Task<IActionResult> CreateDraft(Guid ticketId, [FromBody] OpsDraftTransactionRequest request, CancellationToken ct)
    {
        var result = await _data.CreateDraftAsync(
            CurrentActorId,
            ticketId,
            new SupportDraftInput(request.AmountCents, request.TransactionType, request.TransactedAt, request.CategoryId, request.MerchantName, request.Description, request.ClientRequestId),
            CurrentIpAddress,
            ct);
        var body = ApiResponse<SupportDraftResult>.Ok(result);
        return result.AlreadyExisted ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }
}

public record OpsAccessRequest(IReadOnlyList<SupportAccessScope>? Scopes, string? Reason);

public record OpsDraftTransactionRequest(
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    Guid? CategoryId,
    string? MerchantName,
    string? Description,
    Guid? ClientRequestId);
