using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Khoản thu/chi định kỳ (lương, tiền nhà, thuê bao): xem, thêm tay, sửa, và quyết định với các đề xuất hệ thống phát hiện.</summary>
[ApiController]
[Route("api/v1/recurring-items")]
[Authorize]
public class RecurringItemsController : ControllerBase
{
    private readonly IRecurringItemService _service;
    private readonly IRecurringInstallmentService _installments;

    public RecurringItemsController(IRecurringItemService service, IRecurringInstallmentService installments)
    {
        _service = service;
        _installments = installments;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Không truyền <paramref name="status"/> thì trả mọi khoản trừ đã hủy, đã từ chối và đã hoàn thành (xem bằng <c>status=Completed</c>).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RecurringStatus? status, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<RecurringItemDto>>.Ok(await _service.ListAsync(CurrentUserId, status, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringItemRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(
            new CreateRecurringItemCommand(
                CurrentUserId, request.Direction, request.Name ?? string.Empty, request.CounterpartyName, request.AmountCents,
                request.Cadence, request.NextDueDate, request.CategoryId, request.FinancialAccountId, request.Installment()),
            ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<RecurringItemDto>.Ok(created));
    }

    /// <summary>Tạo khoản định kỳ từ một giao dịch đã có (tên, số tiền, danh mục, ví lấy từ giao dịch). Body có thể bỏ trống: chu kỳ mặc định hằng tháng.</summary>
    [HttpPost("from-transaction/{transactionId:guid}")]
    public async Task<IActionResult> FromTransaction(Guid transactionId, [FromBody] FromTransactionRequest? request, [FromServices] FinMate.Application.Transactions.ITransactionExtrasService extras, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<RecurringItemDto>.Ok(await extras.CreateRecurringAsync(CurrentUserId, transactionId, request?.Cadence, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRecurringItemRequest request, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.UpdateAsync(
            new UpdateRecurringItemCommand(
                CurrentUserId, id, request.Name, request.AmountCents, request.Cadence, request.NextDueDate, request.CategoryId, request.FinancialAccountId,
                request.Installment(), request.ClearEnd ?? false, request.Status),
            ct)));

    /// <summary>Xác nhận đề xuất. Body tuỳ chọn mang trường kỳ cuối (Phase 92).</summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] InstallmentFieldsRequest? request, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.ConfirmAsync(CurrentUserId, id, request?.Installment(), ct)));

    /// <summary>Các kỳ đã trả còn sống (tracker khớp, đánh dấu tay, tất toán), mới nhất trước.</summary>
    [HttpGet("{id:guid}/payments")]
    public async Task<IActionResult> ListPayments(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<RecurringPaymentDto>>.Ok(await _installments.ListPaymentsAsync(CurrentUserId, id, ct)));

    /// <summary>Đánh dấu kỳ đang đến hạn là đã trả (BR-INS-04): gắn giao dịch có sẵn, hoặc bỏ trống khi trả tiền mặt không ghi sổ.</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid id, [FromBody] RecurringPaymentRequest? request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<RecurringPaymentResultDto>.Ok(
            await _installments.AddPaymentAsync(CurrentUserId, id, request?.TransactionId, ct)));

    [HttpDelete("{id:guid}/payments/{paymentId:guid}")]
    public async Task<IActionResult> RemovePayment(Guid id, Guid paymentId, CancellationToken ct)
    {
        await _installments.RemovePaymentAsync(CurrentUserId, id, paymentId, ct);
        return NoContent();
    }

    /// <summary>Tất toán trước hạn (BR-INS-08); tuỳ chọn gắn giao dịch tất toán. Gọi lại khi đã hoàn thành → 200 như cũ.</summary>
    [HttpPost("{id:guid}/settle")]
    public async Task<IActionResult> Settle(Guid id, [FromBody] RecurringPaymentRequest? request, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _installments.SettleAsync(CurrentUserId, id, request?.TransactionId, ct)));

    /// <summary>Từ chối đề xuất; hệ thống sẽ không đề xuất lại cùng người nhận.</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.RejectAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.PauseAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.ResumeAsync(CurrentUserId, id, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _service.CancelAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}

public record CreateRecurringItemRequest(
    RecurringDirection Direction,
    string? Name,
    string? CounterpartyName,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId,
    int? TotalOccurrences = null,
    DateOnly? EndDate = null,
    int? AnchorDay = null,
    int? PrepaidOccurrences = null)
{
    public InstallmentFields Installment() => new(TotalOccurrences, EndDate, AnchorDay, PrepaidOccurrences);
}

public record UpdateRecurringItemRequest(
    string? Name,
    long? AmountCents,
    RecurringCadence? Cadence,
    DateOnly? NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId,
    int? TotalOccurrences = null,
    DateOnly? EndDate = null,
    int? AnchorDay = null,
    int? PrepaidOccurrences = null,
    bool? ClearEnd = null,
    RecurringStatus? Status = null)
{
    public InstallmentFields Installment() => new(TotalOccurrences, EndDate, AnchorDay, PrepaidOccurrences);
}

/// <summary>Trường kỳ cuối khi xác nhận đề xuất (Phase 92).</summary>
public record InstallmentFieldsRequest(int? TotalOccurrences = null, DateOnly? EndDate = null, int? AnchorDay = null, int? PrepaidOccurrences = null)
{
    public InstallmentFields Installment() => new(TotalOccurrences, EndDate, AnchorDay, PrepaidOccurrences);
}

public record RecurringPaymentRequest(Guid? TransactionId);

public record FromTransactionRequest(RecurringCadence? Cadence);
